# Inventori HANA 0.4.0-review.1

Build tinjauan untuk uji UI/UX dan integrasi awal. Belum disetujui untuk produksi.

## Mulai

1. Ekstrak seluruh ZIP ke folder baru. Jalankan **InventoryApp.exe**. .NET terpisah tidak diperlukan dalam paket Windows x64 ini.
2. Pilih **Masuk cloud** dan gunakan email/kata sandi Supabase. Akun `ezragustinm@gmail.com` telah ditetapkan sebagai Admin di server. Kata sandi tidak disertakan dalam paket.
3. Akun baru dapat memilih **Daftar sebagai Viewer**, lalu memverifikasi email. Admin mengubah peran di **Kelola akun**. Email konfirmasi tunduk pada konfigurasi dan batas pengiriman Supabase.
4. Pilih **Tambah aset**, isi nama barang dan gedung, ruangan opsional, lalu **Simpan dan sinkron**. Kode permanen menggunakan UUID untuk menghindari benturan antar komputer.
5. Periksa indikator antrean. Sinkron berjalan saat masuk/melanjutkan akun, setelah simpan, dan setiap 30 detik untuk mencoba kembali setelah koneksi pulih.

## Uji yang disarankan

- Tambahkan satu barang uji, lalu masuk di komputer kedua dan tekan **Sinkron sekarang**.
- Coba barang bernama sama di gedung/ruangan yang sama. **Periksa Kembali** adalah pilihan awal; **Tetap Tambah** menyatakan barang fisik yang berbeda dan dicatat server.
- Putus koneksi setelah pernah masuk. Gunakan **Buka cache offline** bila memulai ulang. Simpan perubahan, tutup/buka kembali, sambungkan internet dan sinkronkan. Antrean tetap terenkripsi dan terikat akun pembuat.
- Buka aset yang sama di dua komputer. Simpan perubahan dari komputer kedua, kemudian simpan dari komputer pertama. Buka **Antrean / konflik** untuk membaca versi lokal dan server sebelum memilih.
- Coba akun Viewer: data dapat dibaca, sedangkan server menolak penulisan dan promosi diri.
- Buat **Cadangan aman** dengan kata sandi pemulihan yang Anda simpan sendiri. Cadangan memuat cache cloud, antrean, dan inventaris lokal, bukan seluruh instalasi server Supabase.

## Inventaris lama dan spreadsheet

**Inventaris lokal / Excel & referensi** membuka alur lokal yang sudah ada: login lokal, referensi, Excel/CSV, dan audit lokal. Login cloud berbeda dari login lokal.

Data lokal tidak otomatis ditransfer. Untuk transfer yang disengaja: masuk sebagai Admin lokal, buka **Cloud v0.4**, masuk sebagai Admin cloud, kemudian **Transfer inventaris lokal**. Tersedia snapshot sebelum transfer. Kode aset lama dipertahankan dan riwayat aset lama disalin bersama identitas sumbernya. Tidak ada hash kata sandi lokal yang diunggah.

Jika semua data lama tampak kosong, jangan mengisi ulang secara acak: paket baru tidak memuat database pribadi. Gunakan pemulihan cadangan pada inventaris lokal atau lokasi database yang ditetapkan di appsettings.json. Transfer lokal hanya menambahkan aset yang belum ada di cache; perubahan lokal setelah transfer tidak otomatis menjadi perubahan cloud.

## Penyimpanan dan pemulihan

Instalasi baru menyimpan data di `%LOCALAPPDATA%\InventoriHANA\review04`, di luar folder program, sehingga ekstraksi build baru mempertahankan data. Lokasi absolut yang dikonfigurasi dan database yang sudah ada di sebelah program tetap dipakai. Jangan menimpa berkas database/kunci lama ketika mengganti program.

Database menggunakan SQLCipher; kuncinya dilindungi Windows DPAPI. Token cloud juga dilindungi DPAPI dan dihapus saat keluar cloud. Akun Windows yang sudah membuka aplikasi tetap dapat membaca cache. Offline memakai izin login terakhir; server memeriksa kembali peran terkini ketika upload.

Cadangan otomatis dibuat setelah sesi berubah, ketika keluar/beralih akun dan pada startup berikutnya bila sesi terputus. Sepuluh snapshot sesi terbaru dipertahankan setelah cadangan baru lolos verifikasi. Snapshot migrasi disimpan terpisah dan tidak dirotasi. Cadangan otomatis membutuhkan akun Windows dan recovery.key yang sama; cadangan manual dengan kata sandi dapat dipulihkan di komputer lain. Kunci/token tidak boleh dibagikan.

## Batas build tinjauan

- Cloud dan inventaris lokal memiliki tampilan berbeda. Cloud saat ini memakai isian teks untuk kategori/lokasi/kondisi; penyatuan referensi dan input Excel langsung ke cloud belum selesai. Spreadsheet masih melalui pratinjau impor lokal, kemudian transfer Admin.
- Pemulihan penuh server Supabase (termasuk Auth/identities dan Storage) belum diimplementasikan atau diuji. Cadangan cache bukan penggantinya. Jangan menganggap build ini memiliki disaster recovery penuh.
- Pemeriksaan rilis mencoba GitHub saat membuka inventaris cloud. Repositori privat memerlukan akses GitHub di browser; pemeriksaan tanpa kredensial tidak dapat membaca rilis privat. Tombol **Rilis GitHub** membuka halaman rilis. Instalasi otomatis belum tersedia.
- Workflow Windows disertakan untuk menghasilkan ZIP/prerelease melalui GitHub Actions, tetapi publikasi dan hasil Actions belum diverifikasi dalam build lokal ini.
- Uji lokal mencakup retry/koneksi putus/konflik dan pemulihan antrean. Uji server dalam transaksi mencakup izin, signup-role tampering, duplikat, idempotensi dan konflik; fixture dibatalkan. Uji HTTP login dan persistensi dari dua klien nyata belum dijalankan: pembuatan akun uji memerlukan persetujuan.
- NuGet tidak dapat menjangkau umpan audit kerentanan pada lingkungan build (NU1900); build berhasil menggunakan dependensi yang tersedia. Ini bukan hasil audit dependensi bersih.
- Email konfirmasi, pemulihan kata sandi melalui email, batas layanan gratis, dan pemeriksaan kata sandi bocor perlu pemeriksaan konfigurasi sebelum produksi. Adviser melaporkan pemeriksaan kata sandi bocor belum aktif.

**Hubungi Admin** membuka email ke `ezragustinm@gmail.com` pada aplikasi email bawaan, tanpa mengirim otomatis.

Laporkan langkah, hasil yang diharapkan, hasil aktual, serta indikator antrean ketika menemukan masalah. Jangan menyertakan kata sandi atau token.
