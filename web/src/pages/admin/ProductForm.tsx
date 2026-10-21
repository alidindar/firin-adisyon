import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { adminApi, type Category } from '../../api/admin';
import type { Product, UnitType } from '../../api/types';
import { Modal } from '../../components/Modal';
import { Thumb } from '../../components/Thumb';
import { parseTlToKurus } from '../../lib/format';
import { resizeImage } from '../../lib/image';

interface Props {
  product: Product | null;
  categories: Category[];
  onClose: () => void;
}

const toTl = (kurus: number) => (kurus / 100).toFixed(2).replace('.', ',');

export function ProductForm({ product, categories, onClose }: Props) {
  const qc = useQueryClient();
  // Ürün kaydedilip fotoğraf yüklemesi başarısız olursa ikinci "Kaydet" yeni ürün açmasın.
  const savedRef = useRef<Product | null>(product);
  const [name, setName] = useState(product?.name ?? '');
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? categories[0].id);
  const [unitType, setUnitType] = useState<UnitType>(product?.unitType ?? 'Piece');
  const [price, setPrice] = useState(product ? toTl(product.priceKurus) : '');
  const [sortOrder, setSortOrder] = useState(String(product?.sortOrder ?? 0));
  const [isActive, setIsActive] = useState(product?.isActive ?? true);
  const [photo, setPhoto] = useState<Blob | null>(null);
  const [preview, setPreview] = useState<string | null>(product?.photoUrl ?? null);
  const [removePhoto, setRemovePhoto] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => () => {
    if (preview?.startsWith('blob:')) URL.revokeObjectURL(preview);
  }, [preview]);

  const save = useMutation({
    mutationFn: async () => {
      const priceKurus = parseTlToKurus(price);
      if (priceKurus === null) throw new Error('Fiyatı 45 ya da 45,50 biçiminde yazın.');
      const input = { categoryId, name: name.trim(), unitType, priceKurus, isActive, sortOrder: Number(sortOrder) || 0 };
      let saved = savedRef.current
        ? await adminApi.updateProduct(savedRef.current.id, input)
        : await adminApi.createProduct(input);
      savedRef.current = saved;
      if (photo) saved = await adminApi.uploadPhoto(saved.id, photo);
      else if (removePhoto && saved.photoUrl) saved = await adminApi.deletePhoto(saved.id);
      return saved;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin'] });
      qc.invalidateQueries({ queryKey: ['catalog'] });
      onClose();
    },
    onError: (e) => setError(e.message),
  });

  const pickPhoto = async (file: File | undefined) => {
    if (!file) return;
    try {
      const blob = await resizeImage(file);
      setPhoto(blob);
      setRemovePhoto(false);
      setPreview(URL.createObjectURL(blob));
    } catch {
      setError('Fotoğraf okunamadı.');
    }
  };

  return (
    <Modal title={product ? 'Ürünü düzenle' : 'Yeni ürün'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Ürün adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={60} autoFocus /></label>
        <label>
          Kategori
          <select value={categoryId} onChange={(e) => setCategoryId(Number(e.target.value))}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <fieldset className="segmented">
          <legend>Satış birimi</legend>
          <label><input type="radio" name="unit" checked={unitType === 'Piece'} onChange={() => setUnitType('Piece')} /> Adet</label>
          <label><input type="radio" name="unit" checked={unitType === 'Gram'} onChange={() => setUnitType('Gram')} /> Gram (tartılarak)</label>
        </fieldset>
        <label>
          {unitType === 'Piece' ? 'Adet fiyatı (₺)' : 'Kilogram fiyatı (₺)'}
          <input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required
            placeholder={unitType === 'Piece' ? '25,00' : '600,00'} />
        </label>
        <label>Sıra (küçük olan önce görünür)<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Satışta</label>
        <div className="photo-field">
          <Thumb url={preview} large />
          <label className="button-like">
            Fotoğraf seç
            <input type="file" accept="image/*" hidden onChange={(e) => pickPhoto(e.target.files?.[0])} />
          </label>
          {preview && (
            <button type="button" className="link" onClick={() => { setPhoto(null); setPreview(null); setRemovePhoto(true); }}>
              Fotoğrafı kaldır
            </button>
          )}
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>{save.isPending ? 'Kaydediliyor…' : 'Kaydet'}</button>
      </form>
    </Modal>
  );
}
