# Fırın Adisyon

Fırın ve kafe için masa adisyon uygulaması. Kasa bilgisayarında (Windows) çalışan bir sunucudan oluşur; tablet ve telefonlar fırının Wi-Fi ağından tarayıcıyla bağlanır.

- Kurulum ve kullanım: [docs/KURULUM.md](docs/KURULUM.md)
- Tasarım: [docs/superpowers/specs/2026-10-21-firin-adisyon-design.md](docs/superpowers/specs/2026-10-21-firin-adisyon-design.md)

## Geliştirme

    dotnet run --project server/Firin.Api      # http://localhost:5080, veriler repo kökündeki data/ (git'te yok)
    cd web && npm run dev                      # http://localhost:5173 (API'ye proxy)
    dotnet test server/Firin.Tests
    cd web && npm test

`dotnet run` launch profilini kullanır (`Firin__DataDir=../../data`). `--no-launch-profile` ile çalıştırırsanız veri klasörü derleme çıktısının yanındaki `data` olur.

Yayın (kasa PC için self-contained): `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`

Teknoloji: ASP.NET Core 10 Minimal API, EF Core + SQLite, SignalR; React + TypeScript + Vite, TanStack Query.
