using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Dapper;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Data.Sqlite;
using InventoryApp.Data;

namespace InventoryApp.Services;

public class ImportRow
{
    public int SourceRow {get;set;}
    public string Name {get;set;}="";
    public string Category {get;set;}="";
    public string Building {get;set;}="";
    public string Room {get;set;}="";
    public string Quantity {get;set;}="1";
    public string Year {get;set;}="";
    public string Condition {get;set;}="";
    public string LegacyCode {get;set;}="";
    public string Notes {get;set;}="";
    public string Description {get;set;}="";
    public string Color {get;set;}="";
    public string Photo {get;set;}="";
    public string Problem {get;set;}="";
}
public record SheetData(string[] Headers,List<string[]> Rows);

public class SpreadsheetService
{
    public static readonly string[] Fields={"Name","Category","Building","Room","Quantity","Year","Condition","LegacyCode","Notes","Description","Color","Photo"};
    public static readonly string[] Labels={"Nama barang *","Kategori *","Gedung *","Ruangan","Jumlah barang","Tahun perolehan","Kondisi","Kode lama","Keterangan","Tipe/deskripsi","Warna","Foto (referensi)"};
    public static string[] Sheets(string path)
    {
        if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase)) return new[]{"CSV"};
        using var book=new XLWorkbook(path); return book.Worksheets.Select(s=>s.Name).ToArray();
    }
    public static SheetData Read(string path,string sheet,string delimiter=",")
    {
        if(new FileInfo(path).Length>30_000_000) throw new ArgumentException("Batas berkas 30 MB. Bagi berkas besar menjadi beberapa bagian.");
        var rows=new List<string[]>();
        if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
        {
            using var reader=new TextFieldParser(path,Encoding.UTF8,true) { TextFieldType=FieldType.Delimited,HasFieldsEnclosedInQuotes=true };
            reader.SetDelimiters(delimiter);
            while(!reader.EndOfData && rows.Count<10002) rows.Add(reader.ReadFields() ?? Array.Empty<string>());
        }
        else
        {
            using var book=new XLWorkbook(path); var ws=book.Worksheet(sheet);
            var last=ws.LastRowUsed()?.RowNumber()??0; var width=ws.LastColumnUsed()?.ColumnNumber()??0;
            if(width>100 || last>10001) throw new ArgumentException("Maksimal 100 kolom dan 10.000 baris data.");
            for(var r=1;r<=last;r++) rows.Add(Enumerable.Range(1,width).Select(c=>ws.Cell(r,c).GetFormattedString()).ToArray());
        }
        if(rows.Count<2 || rows.Count>10001) throw new ArgumentException("Berkas harus memiliki header di baris pertama dan 1–10.000 baris data.");
        return new(rows[0],rows.Skip(1).ToList());
    }
    public static int Guess(string field,string[] headers)
    {
        string Normalize(string x)=>new string(x.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var aliases=field switch {
            "Name"=>new[]{"namabarang","nama","itemname"},"Category"=>new[]{"tipegroup","kategori","category"},
            "Building"=>new[]{"gedung","building"},"Room"=>new[]{"ruanganbarang","ruangan","room"},
            "Quantity"=>new[]{"jumlahbarang","jumlah","quantity","qty"},"Year"=>new[]{"tahunperolehan","tahunperolehna","tahun"},
            "Condition"=>new[]{"kondisi","condition"},"LegacyCode"=>new[]{"kode","kodelama","legacycode"},
            "Notes"=>new[]{"keterangan","notes"},"Description"=>new[]{"tipedeskripsi","deskripsi"},"Color"=>new[]{"warna","color"},
            _=>new[]{"foto","photo"}};
        return Array.FindIndex(headers,h=>aliases.Contains(Normalize(h)));
    }
    public static List<ImportRow> Map(SheetData sheet,int[] map)
    {
        var result=new List<ImportRow>();
        for(var index=0;index<sheet.Rows.Count;index++)
        {
            var cells=sheet.Rows[index]; if(cells.All(string.IsNullOrWhiteSpace)) continue;
            var row=new ImportRow {SourceRow=index+2};
            for(var f=0;f<Fields.Length;f++)
            {
                var col=map[f]; var value=col>=0 && col<cells.Length?cells[col].Trim():"";
                if(Fields[f]=="Quantity" && col<0) value="1";
                typeof(ImportRow).GetProperty(Fields[f])!.SetValue(row,value);
            }
            result.Add(row);
        }
        return result;
    }
    public static string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    public static bool Validate(List<ImportRow> rows,bool allowNew,bool allowSimilar)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        using var conn=Db.Open();
        return ValidateRows(conn,null,rows,allowNew,allowSimilar);
    }
    private static bool ValidateRows(SqliteConnection c,SqliteTransaction? tx,List<ImportRow> rows,bool allowNew,bool allowSimilar)
    {
        var seenCodes=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenRows=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemDetails=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        long total=0;
        foreach(var row in rows)
        {
            foreach(var field in Fields){var property=typeof(ImportRow).GetProperty(field)!;property.SetValue(row,((string?)property.GetValue(row)??"").Trim());}
            var errors=new List<string>();
            if(string.IsNullOrWhiteSpace(row.Name)||string.IsNullOrWhiteSpace(row.Building)||string.IsNullOrWhiteSpace(row.Category)) errors.Add("Nama, kategori, dan gedung wajib");
            if(!int.TryParse(row.Quantity,out var qty)||qty<1||qty>10000) errors.Add("Jumlah harus 1–10.000"); else total+=qty;
            if(row.Year!="" && (!int.TryParse(row.Year,out var year)||year<1900||year>2100)) errors.Add("Tahun tidak valid");
            if(row.Name.Length>150 || row.Category.Length>150 || row.Building.Length>150 || row.Room.Length>150) errors.Add("Nama maksimal 150 karakter");
            if(row.Condition!="" && c.ExecuteScalar<int>("SELECT COUNT(*) FROM conditions WHERE (code=@v COLLATE NOCASE OR name=@v COLLATE NOCASE) AND is_active=1",new {v=row.Condition},tx)!=1) errors.Add("Kondisi belum dikenal: "+row.Condition);
            if(row.LegacyCode!="" && (!seenCodes.Add(row.LegacyCode)||c.ExecuteScalar<int>("SELECT COUNT(*) FROM assets WHERE legacy_code=@v",new{v=row.LegacyCode},tx)>0)) errors.Add("Kode lama berulang/sudah diimpor");
            var signature=$"{row.Category}|{row.Name}|{row.Building}|{row.Room}|{row.Year}|{row.Condition}";
            if(!allowSimilar && !seenRows.Add(signature)) errors.Add("Baris mirip; periksa atau izinkan baris mirip");
            var itemKey=row.Category+"\0"+row.Name;var details=row.Description+"\0"+row.Color;
            if(itemDetails.TryGetValue(itemKey,out var previous) && previous!=details)errors.Add("Nama barang sama tetapi deskripsi/warna berbeda di berkas ini; gunakan nama yang berbeda");
            else itemDetails[itemKey]=details;
            if(!allowSimilar && row.LegacyCode=="" && c.ExecuteScalar<int>(@"SELECT COUNT(*) FROM v_asset_list WHERE ItemTypeName=@Name COLLATE NOCASE AND CategoryName=@Category COLLATE NOCASE AND BuildingName=@Building COLLATE NOCASE AND COALESCE(RoomName,'')=@Room COLLATE NOCASE",row,tx)>0)
                errors.Add("Barang/lokasi mirip sudah ada; periksa atau izinkan baris mirip");
            foreach(var pair in new[]{("categories",row.Category),("buildings",row.Building)})
            {
                if(c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {pair.Item1} WHERE name=@v COLLATE NOCASE AND is_active=1",new{v=pair.Item2},tx)==0 && !allowNew) errors.Add("Referensi baru: "+pair.Item2);
                if(c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {pair.Item1} WHERE name=@v COLLATE NOCASE AND is_active=0",new{v=pair.Item2},tx)>0) errors.Add("Referensi nonaktif: "+pair.Item2);
            }
            var item=c.QueryFirstOrDefault<dynamic>(@"SELECT i.type_description Description,i.color Color,i.status Status FROM item_types i JOIN categories c ON c.id=i.category_id WHERE i.name=@Name COLLATE NOCASE AND c.name=@Category COLLATE NOCASE",row,tx);
            if(item is null && !allowNew) errors.Add("Jenis barang baru");
            if(item is not null && ((string)item.Status!="ACTIVE" || (row.Description!="" && row.Description!=((string?)item.Description??"")) || (row.Color!="" && row.Color!=((string?)item.Color??"")))) errors.Add("Jenis barang berbeda/nonaktif; periksa nama, deskripsi, warna");
            if(row.Room!="")
            {
                var state=c.ExecuteScalar<long?>("SELECT r.is_active FROM rooms r JOIN buildings b ON b.id=r.building_id WHERE r.name=@Room COLLATE NOCASE AND b.name=@Building COLLATE NOCASE",row,tx);
                if(state==0 || (state is null && !allowNew)) errors.Add("Ruangan baru/nonaktif");
            }
            row.Problem=string.Join("; ",errors);
        }
        if(total>10000 && rows.Count>0) rows[0].Problem+="; Maksimal 10.000 aset per impor";
        return rows.Count>0 && rows.All(r=>r.Problem=="");
    }
    public static int Import(List<ImportRow> rows,string sourceName,string fingerprint,bool allowNew,bool allowSimilar)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        using var c=Db.Open(); using var tx=c.BeginTransaction();
        if(c.ExecuteScalar<int>("SELECT COUNT(*) FROM import_batches WHERE hash=@fingerprint",new {fingerprint},tx)>0) throw new ArgumentException("Berkas ini sudah diimpor.");
        if(!ValidateRows(c,tx,rows,allowNew,allowSimilar)) throw new ArgumentException("Masih ada masalah. Periksa pratinjau.");
        var user=Session.User.Id; var audit=new AuditService(); var count=0;
        if(allowSimilar)audit.Record(c,tx,"Import",0,null,"DUPLICATE_OVERRIDE",user,
            note:$"Tetap Tambah untuk impor {sourceName}; sidik berkas {fingerprint}; baris {string.Join(",",rows.Select(r=>r.SourceRow))}");
        long Reference(string table,string name,string entity)
        {
            var id=c.ExecuteScalar<long?>($"SELECT id FROM {table} WHERE name=@name COLLATE NOCASE",new{name},tx);
            if(id is not null) return id.Value;
            c.Execute($"INSERT INTO {table}(name) VALUES(@name)",new{name},tx);
            var added=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
            audit.Record(c,tx,entity,added,null,"CREATE",user,newValue:name,note:"Dari impor"); return added;
        }
        foreach(var r in rows)
        {
            var category=Reference("categories",r.Category,"Category"); var building=Reference("buildings",r.Building,"Building");
            long? room=null;
            if(r.Room!="")
            {
                room=c.ExecuteScalar<long?>("SELECT id FROM rooms WHERE building_id=@building AND name=@Room COLLATE NOCASE",new {building,r.Room},tx);
                if(room is null) { c.Execute("INSERT INTO rooms(building_id,name) VALUES(@building,@Room)",new {building,r.Room},tx);room=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);audit.Record(c,tx,"Room",room.Value,null,"CREATE",user,newValue:r.Room,note:r.Building); }
            }
            var item=c.ExecuteScalar<long?>("SELECT id FROM item_types WHERE category_id=@category AND name=@Name COLLATE NOCASE",new{category,r.Name},tx);
            if(item is null)
            {
                c.Execute("INSERT INTO item_types(category_id,name,type_description,color,code_prefix,created_by) VALUES(@category,@Name,@Description,@Color,'AST',@user)",new{category,r.Name,r.Description,r.Color,user},tx);
                item=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);audit.Record(c,tx,"ItemType",item.Value,null,"CREATE",user,newValue:r.Name);
            }
            var prefix=c.ExecuteScalar<string>("SELECT code_prefix FROM item_types WHERE id=@item",new{item},tx)!;
            var condition=r.Condition=="" ? (long?)null : c.ExecuteScalar<long>("SELECT id FROM conditions WHERE code=@v COLLATE NOCASE OR name=@v COLLATE NOCASE",new{v=r.Condition},tx);
            foreach(var code in AssetCodeGenerator.Reserve(c,tx,prefix,int.Parse(r.Quantity)))
            {
                c.Execute(@"INSERT INTO assets(asset_code,item_type_id,building_id,room_id,condition_id,acquisition_year,notes,legacy_code,legacy_source_file,legacy_row_number,photo_path,created_by,updated_by)
                    VALUES(@code,@item,@building,@room,@condition,@year,@Notes,@LegacyCode,@sourceName,@SourceRow,@Photo,@user,@user)",
                    new{code,item,building,room,condition,year=r.Year==""?(int?)null:int.Parse(r.Year),r.Notes,r.LegacyCode,sourceName,r.SourceRow,r.Photo,user},tx);
                var id=c.ExecuteScalar<long>("SELECT last_insert_rowid()",transaction:tx);
                c.Execute("INSERT INTO identifiers(asset_id,identifier_type,value) VALUES(@id,'QR',@code)",new{id,code},tx);
                audit.Record(c,tx,"Asset",id,code,"CREATE",user,note:$"Impor {sourceName}, baris {r.SourceRow}");count++;
            }
        }
        c.Execute("INSERT INTO import_batches(hash,source) VALUES(@fingerprint,@sourceName)",new{fingerprint,sourceName},tx);
        audit.Record(c,tx,"Import",0,null,"IMPORT",user,note:$"{sourceName}: {rows.Count} baris, {count} aset");tx.Commit();return count;
    }
    public static void Export(string path)
    {
        AccessControl.RequireAdmin(Session.User.Id);
        using var c=Db.Open();
        var data=c.Query(@"SELECT a.asset_code AS [Kode Aset],i.name AS [Nama Barang],c.name AS [Tipe Group],
            b.name AS Gedung,r.name AS [Ruangan Barang],1 AS [Jumlah Barang],a.acquisition_year AS [Tahun Perolehan],
            co.code AS Kondisi,a.legacy_code AS [Kode Lama],a.notes AS Keterangan,i.type_description AS [Tipe/Deskripsi],i.color AS Warna,a.photo_path AS Foto,a.status AS Status
            FROM assets a JOIN item_types i ON i.id=a.item_type_id JOIN categories c ON c.id=i.category_id
            LEFT JOIN buildings b ON b.id=a.building_id LEFT JOIN rooms r ON r.id=a.room_id LEFT JOIN conditions co ON co.id=a.condition_id ORDER BY a.asset_code")
            .Select(row=>(IDictionary<string,object>)row).ToList();
        var headers=new[]{"Kode Aset","Nama Barang","Tipe Group","Gedung","Ruangan Barang","Jumlah Barang","Tahun Perolehan","Kondisi","Kode Lama","Keterangan","Tipe/Deskripsi","Warna","Foto","Status"};
        string Text(object? value)=>Convert.ToString(value,CultureInfo.InvariantCulture)??"";
        if(Path.GetExtension(path).Equals(".csv",StringComparison.OrdinalIgnoreCase))
        {
            string Csv(string v) { if(v.TrimStart().StartsWith('=')||v.TrimStart().StartsWith('+')||v.TrimStart().StartsWith('-')||v.TrimStart().StartsWith('@')) v="'"+v; return "\""+v.Replace("\"","\"\"")+"\""; }
            using var writer=new StreamWriter(path,false,new UTF8Encoding(true)); writer.WriteLine(string.Join(",",headers.Select(Csv)));
            foreach(var row in data) writer.WriteLine(string.Join(",",headers.Select(h=>Csv(Text(row[h])))));
        }
        else
        {
            using var book=new XLWorkbook();var sheet=book.AddWorksheet("Inventaris");
            for(var i=0;i<headers.Length;i++) sheet.Cell(1,i+1).Value=headers[i];
            for(var r=0;r<data.Count;r++) for(var col=0;col<headers.Length;col++) sheet.Cell(r+2,col+1).Value=Text(data[r][headers[col]]);
            sheet.Row(1).Style.Font.Bold=true; sheet.SheetView.FreezeRows(1);sheet.Columns().Width=23;book.SaveAs(path);
        }
        new AuditService().Record("Export",0,null,"EXPORT",Session.User.Id,note:$"{data.Count} aset diekspor");
    }
}
