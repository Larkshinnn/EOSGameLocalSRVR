# PGR Local Server

Server lokal pribadi untuk **Punishing: Gray Raven (PGR)**, berbasis [AscNet](https://github.com/rafi1212122/AscNet) dan disesuaikan untuk eksperimen client PC/Steam. Proyek ini berjalan di komputer sendiri dengan MongoDB lokal; ini bukan server resmi Kuro Games.

> Target konfigurasi saat ini: client PC/Steam **4.8.0** (document version **4.8.12**), channel **205**. Dukungan protokol dan gameplay berbeda-beda per fitur dan belum setara dengan server retail.

## Fitur saat ini

- Server SDK/HTTP dan game TCP dengan akun lokal, login, konfigurasi client, notice, serta data pemain yang tersimpan di MongoDB.
- Bridge client Steam/PC melalui `run_steam.py` dan `proxy.py`: menjalankan server, pengecekan konfigurasi, opsi MongoDB lokal, routing traffic, dan peluncuran client.
- Sistem akun dan progres pemain: karakter, level/skill, senjata, memori, item, stage, event, hadiah, serta notifikasi login.
- Dukungan kompatibilitas untuk draw dan pity, event, sign-in, equipment, dorm, mission, dan beberapa mode permainan.
- Mode yang sudah memiliki implementasi server mencakup Theatre, Theatre3–6, Circuit Connect, dan Babylonia. Kedalaman implementasi dan batas kompatibilitas setiap mode tidak sama.
- Launcher native untuk konfigurasi client yang didukung. Dukungan targetnya mencakup EN, TW, KR, JP, dan CN; ketersediaan konfigurasi tidak berarti semua region telah diuji dengan client aslinya.

Banyak fitur menggunakan tabel client sebagai sumber data; beberapa aturan server yang tidak tersedia memakai kebijakan lokal AscNet. Contohnya, leaderboard hanya berlaku di server lokal. Banner Fate yang aturan pity/odds-nya belum diketahui dinonaktifkan. Implementasi lokal bukan klaim bahwa perilakunya sama dengan retail.

## Kebutuhan

- .NET SDK 8
- MongoDB
- Python 3.10 atau lebih baru untuk bridge
- mitmproxy/mitmdump untuk mode bridge
- Client PGR PC/Steam yang sesuai dengan target kompatibilitas

## Menjalankan server

Jalankan MongoDB terlebih dahulu, lalu dari folder proyek:

```bash
dotnet run --project AscNet/AscNet.csproj -- --urls http://127.0.0.1:8080
```

Pengaturan utama tersedia di `Resources/Configs/config.json`. Tanpa override, server game memakai `127.0.0.1:2335`, MongoDB `127.0.0.1:27017`, dan database `asc_net`.

## Menjalankan bridge Steam/PC

```bash
python3 run_steam.py --with-mongo
```

Untuk menjalankan client setelah server dan proxy siap:

```bash
python3 run_steam.py --with-mongo --launch-cmd ./launch-pgr-ascnet.sh
```

Lihat semua opsi dengan `python3 run_steam.py --help`. Contoh launcher `.sh` memakai path workstation macOS/CrossOver dan perlu disesuaikan sebelum digunakan di mesin lain. Di Windows, gunakan path cache KRSDK client yang benar bila mengaktifkan opsi perbaikan/seeding cache.

## GM Tool

[PGR GM Tool](../pgr-gm-tool/README.md) adalah dashboard terpisah untuk mengelola akun dan data pemain di database lokal yang sama. Jalankan dan konfigurasikan tool itu secara terpisah; tool ini tidak terhubung ke akun server resmi.

## Verifikasi

Harness kompatibilitas:

```bash
dotnet run --project AscNet.Test/AscNet.Test.csproj
```

Harness menyediakan pemeriksaan terfokus melalui opsi `--...-only`; daftar opsi tersedia di `AscNet.Test/Program.cs`. Build proyek utama dengan `dotnet build AscNet.sln`.

Hasil pemeriksaan kompatibilitas tidak sama dengan playthrough native penuh. Pertarungan retail, tampilan UI yang dirender, dan pemutaran movie belum semuanya diverifikasi.

## Data lokal

Jangan commit data runtime seperti `.runtime/`, file database MongoDB, log proxy/server, packet capture, binary client, atau kredensial. Folder build `bin/` dan `obj/` juga merupakan output lokal.

## Kredit

Proyek ini merupakan pengembangan lokal berbasis [AscNet](https://github.com/rafi1212122/AscNet). Tabel dan kontrak client berasal dari resource client PGR; hak atas game dan asetnya tetap milik pemegang hak masing-masing.
