import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { useState } from 'react';
import { adminApi, type SettingsRow } from '../../api/admin';
import { formatDateTime } from '../../lib/dates';

export function SettingsAdmin() {
  const settings = useQuery({ queryKey: ['admin', 'settings'], queryFn: adminApi.settings });
  if (settings.isPending) return <p>Yükleniyor…</p>;
  if (settings.isError) return <p className="error">{settings.error.message}</p>;
  return (
    <div className="settings">
      <SettingsForm initial={settings.data} />
      <Connection urls={settings.data.lanUrls} />
      <Backups settings={settings.data} />
    </div>
  );
}

function SettingsForm({ initial }: { initial: SettingsRow }) {
  const qc = useQueryClient();
  const [name, setName] = useState(initial.businessName);
  const [dir, setDir] = useState(initial.backupDir ?? '');
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const save = useMutation({
    // Sunucu null/boş değeri "varsayılana dön" sayar; bu yüzden her zaman iki alan birlikte gönderilir.
    mutationFn: () => adminApi.saveSettings({ businessName: name.trim() || null, backupDir: dir.trim() || null }),
    onSuccess: (s) => {
      qc.setQueryData(['admin', 'settings'], s);
      qc.invalidateQueries({ queryKey: ['info'] });
      qc.invalidateQueries({ queryKey: ['admin', 'backups'] });
      setMsg({ ok: true, text: 'Kaydedildi.' });
    },
    onError: (e) => setMsg({ ok: false, text: e.message }),
  });

  return (
    <section>
      <h2>İşletme</h2>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setMsg(null); save.mutate(); }}>
        <label>İşletme adı (giriş ekranında görünür)<input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} /></label>
        <label>
          Yedek klasörü (boş bırakılırsa programın kendi klasörü)
          <input value={dir} onChange={(e) => setDir(e.target.value)} placeholder="E:\FirinYedek" autoCapitalize="none" autoCorrect="off" spellCheck={false} />
        </label>
        <p className="hint">Bilgisayar bozulursa veriler kaybolmasın diye yedek klasörü olarak bir USB bellek ya da harici disk seçin. Alanı boş bırakıp kaydederseniz programın kendi klasörüne dönülür. Eşlenmiş ağ sürücüleri (Z: gibi) çalışmaz.</p>
        {msg && <p className={msg.ok ? 'ok' : 'error'} role="status">{msg.text}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </section>
  );
}

function Connection({ urls }: { urls: string[] }) {
  return (
    <section>
      <h2>Tablet ve telefon bağlantısı</h2>
      {urls.length === 0 ? (
        <p className="error">Bu bilgisayarın ağ adresi bulunamadı. Wi-Fi ya da kablo bağlantısını kontrol edin.</p>
      ) : (
        <>
          <p className="hint">Tablet ya da telefon fırının Wi-Fi ağına bağlıyken kamerayla bu kodu okutun. Açılan sayfayı tarayıcı menüsünden "Ana ekrana ekle" ile uygulama gibi kaydedin.</p>
          <div className="qr-list">
            {urls.map((u) => (
              <figure key={u} className="qr">
                <QRCodeSVG value={u} size={160} marginSize={2} />
                <figcaption>{u}</figcaption>
              </figure>
            ))}
          </div>
          <p className="hint">Adres değişmesin diye modem ayarlarından bu bilgisayara sabit IP verin (DHCP rezervasyonu). Ayrıntılar kurulum rehberinde.</p>
        </>
      )}
    </section>
  );
}

function Backups({ settings }: { settings: SettingsRow }) {
  const qc = useQueryClient();
  const backups = useQuery({ queryKey: ['admin', 'backups'], queryFn: adminApi.backups });
  const [error, setError] = useState<string | null>(null);
  const create = useMutation({
    mutationFn: adminApi.createBackup,
    onSuccess: () => {
      setError(null);
      qc.invalidateQueries({ queryKey: ['admin', 'backups'] });
      qc.invalidateQueries({ queryKey: ['admin', 'settings'] });
    },
    onError: (e) => {
      setError(e.message);
      qc.invalidateQueries({ queryKey: ['admin', 'settings'] });
    },
  });

  return (
    <section>
      <div className="toolbar">
        <h2>Yedekler</h2>
        <button className="primary" disabled={create.isPending} onClick={() => { setError(null); create.mutate(); }}>
          {create.isPending ? 'Yedekleniyor…' : 'Yedek al'}
        </button>
      </div>
      <p className="hint">
        Her gün otomatik yedek alınır, 30 gün saklanır. Klasör: <code>{settings.effectiveBackupDir}</code>
      </p>
      <p className="hint">
        Son yedek: {settings.lastBackupAt ? formatDateTime(settings.lastBackupAt) : 'henüz yok'}
      </p>
      <p className="hint">Ürün fotoğrafları veritabanı yedeğinin içinde değildir; fotoğraf klasörünü ayrıca yedeklemek için kurulum rehberine (KURULUM) bakın.</p>
      {settings.lastBackupError && <p className="error">Son yedek başarısız: {settings.lastBackupError}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {backups.isError && <p className="error">{backups.error.message}</p>}
      {backups.data && backups.data.length === 0 && <p className="muted">Henüz yedek yok.</p>}
      <ul className="admin-list">
        {backups.data?.slice(0, 10).map((b) => (
          <li key={b.fileName}>
            <a className="row-btn backup-link" href={`/api/admin/backups/${encodeURIComponent(b.fileName)}`} download>
              <span className="row-main">
                <span>{formatDateTime(b.createdAt)}</span>
                <small>{b.fileName} · {Math.max(1, Math.round(b.sizeBytes / 1024))} KB · indir</small>
              </span>
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}
