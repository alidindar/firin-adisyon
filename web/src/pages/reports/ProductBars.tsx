import type { ProductSales } from '../../api/reports';
import { formatKurus, formatQuantity } from '../../lib/format';

const TOP = 20;

export function ProductBars({ products }: { products: ProductSales[] }) {
  if (products.length === 0) return <p className="muted">Bu aralıkta kapanan hesap yok.</p>;
  const top = products.slice(0, TOP);
  const max = top[0].totalKurus || 1;

  return (
    <figure className="chart">
      <figcaption className="chart-title">Ürün bazında satış (ciroya göre)</figcaption>
      <ol className="hbars">
        {top.map((p) => (
          <li key={`${p.productId}-${p.unitType}`} title={`${p.productName}: ${formatKurus(p.totalKurus)}`}>
            <span className="hbar-name">{p.productName}</span>
            <span className="hbar-track">
              <span className="hbar" style={{ width: `${(p.totalKurus / max) * 100}%` }} />
            </span>
            <span className="hbar-value">
              {formatKurus(p.totalKurus)} <small className="muted">· {formatQuantity(p.unitType, p.quantity)}</small>
            </span>
          </li>
        ))}
      </ol>
      <details className="table-view">
        <summary>Tüm ürünler ({products.length})</summary>
        <table>
          <thead><tr><th>Ürün</th><th>Miktar</th><th>Ciro</th></tr></thead>
          <tbody>
            {products.map((p) => (
              <tr key={`${p.productId}-${p.unitType}`}>
                <td>{p.productName}</td>
                <td>{formatQuantity(p.unitType, p.quantity)}</td>
                <td>{formatKurus(p.totalKurus)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </details>
    </figure>
  );
}
