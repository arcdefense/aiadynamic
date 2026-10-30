# 🏝️ Dynamic Island for Windows (Docked Notch & Real Spectrum)

Aplikasi **Dynamic Island** untuk Windows 10 & 11 yang terintegrasi langsung dengan sistem operasi Windows. Dilengkapi dengan **Auto-Hide (Anti-Halang Tab Browser)** dan **Spektrum Audio Perangkat Keras Asli (Real-Time Hardware FFT)**.

---

## ✨ Fitur Unggulan Terbaru

1. **Auto-Hide / Stealth Peek Notch (Anti-Halang Tab)**
   * Saat tidak dipakai, Dynamic Island mengecil menjadi garis tipis `72 x 3 px` yang menempel di bezel paling atas.
   * **Tab browser (Chrome, Edge, Firefox) 100% terlihat jelas dan bebas diklik tanpa terhalang.**
   * Cukup arahkan kursor mentok ke atas layar untuk memunculkan Dynamic Island secara instan.

2. **Spektrum Audio ASLI (Real-Time 8-Band Hardware FFT)**
   * Menggunakan **Windows Core Audio (WASAPI Loopback Capture)** dan **256-sample Cooley-Tukey FFT**.
   * Membaca frekuensi nyata dari speaker/headphone:
     * Bass & Sub-bass (Kick drum / Bassline)
     * Midrange (Gitar & Vokal)
     * Treble / Highs (Hi-hats & Simbal)
   * Dilengkapi algoritma *fluid exponential decay* agar pergerakan batang equalizer mulus seperti mixer studio.

3. **Posisi Spektrum Baru (Di Samping Album Cover)**
   * Saat kartu terbuka (*expanded*), 8 batang spektrum diletakkan langsung **di samping kanan cover album lagu**.

4. **Deteksi Otomatis Nama Aplikasi Media**
   * Tidak lagi bertuliskan teks generik, melainkan otomatis mendeteksi:
     * `SPOTIFY`
     * `GOOGLE CHROME`
     * `MICROSOFT EDGE`
     * `APPLE MUSIC`
     * `FIREFOX` / `BRAVE`
     * `VLC PLAYER`
     * dan pemutar media Windows lainnya.

5. **Tombol Pengaturan Spektrum (Ikon Gear di Samping Pin 📌)**
   * **5 Tema Warna Spektrum**:
     - 🟢 **Neo Green** (Default Spotify/iOS)
     - 🌈 **Rainbow Spectrum** (Gradasi Merah ke Ungu)
     - 🔵 **Cyber Cyan / Blue**
     - ⚪ **Minimalist White** (Clean Apple UI)
     - 🟠 **Sunset Amber / Fire**
   * **Sensitivitas Spektrum**:
     - Normal (1.0x)
     - High (1.5x)
     - Ultra Boost (2.0x)

---

## 🎮 Kontrol & Interaksi

| Aksi | Fungsi |
|---|---|
| **Arahkan Mouse ke Atas** | Membuka pulau dari mode sembunyi (*Peek Notch*) |
| **Mouse Keluar** | Otomatis melipat diri kembali ke bezel atas (delay ~650ms) |
| **Klik 1x pada Kapsul** | Memperluas ke mode kartu (*Expanded Media Card*) |
| **Double Click** | Pin / Mengunci agar tidak otomatis sembunyi |
| **Ikon Gear ⚙️** | Mengubah tema warna dan sensitivitas spektrum audio |
| **Ikon Pin 📌** | Kunci posisi terbuka |
| **Shift + Drag Mouse** | Menggeser posisi pulau di layar |
| **Klik Kanan** | Menu konteks lengkap (Pin, Auto-Hide, Kontrol Media, Refresh, Exit) |

---

## 🚀 Cara Menjalankan

Cukup jalankan salah satu:
* Double-click [`publish/DynamicIsland.exe`](file:///D:/Projek-dev/aia-ide/publish/DynamicIsland.exe) (Portable Self-Contained)
* Atau jalankan [`run.bat`](file:///D:/Projek-dev/aia-ide/run.bat)
