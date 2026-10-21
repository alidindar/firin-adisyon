import type { Me } from '../api/types';

export function HomePage({ me }: { me: Me }) {
  return (
    <div className="page">
      <h1>Hoş geldin, {me.name}</h1>
      <p>Masalar ekranı bir sonraki adımda buraya gelecek.</p>
    </div>
  );
}
