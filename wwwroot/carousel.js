/* Motor independiente del carrusel: rotación, aleatoriedad y no repetir primer negocio.
   No consulta ni modifica SQL Server. */
(function (root, factory) {
  const tools = factory();
  if (typeof module === 'object' && module.exports) module.exports = tools;
  else root.CiticioCarousel = tools;
})(typeof window !== 'undefined' ? window : globalThis, function () {
  function uniqueSlides(items) {
    const matches = new Map();
    for (const row of items || []) {
      if (!matches.has(row.id_empresa)) matches.set(row.id_empresa, row);
      else {
        const old = matches.get(row.id_empresa);
        const oldPic = Number(!!(old.imagen_portada_url || old.imagen_sucursal || old.imagen_servicio));
        const newPic = Number(!!(row.imagen_portada_url || row.imagen_sucursal || row.imagen_servicio));
        if (newPic > oldPic) matches.set(row.id_empresa, row);
      }
    }
    return Array.from(matches.values());
  }
  function shuffle(items, random = Math.random) {
    const out = [...items];
    for (let i = out.length - 1; i > 0; i--) {
      const j = Math.floor(random() * (i + 1));
      [out[i], out[j]] = [out[j], out[i]];
    }
    return out;
  }
  function prepare(items, previousId = null, random = Math.random) {
    const unique = uniqueSlides(items);
    const preferred = unique.filter(row => row.imagen_portada_url || row.imagen_sucursal || row.imagen_servicio);
    const other = unique.filter(row => !preferred.includes(row));
    const ordered = [...shuffle(preferred, random), ...shuffle(other, random)];
    if (ordered.length > 1 && String(ordered[0].id_empresa) === String(previousId)) {
      const otherIndex = ordered.findIndex(row => String(row.id_empresa) !== String(previousId));
      [ordered[0], ordered[otherIndex]] = [ordered[otherIndex], ordered[0]];
    }
    return ordered.slice(0, 24);
  }
  function nextIndex(index, count, step = 1) {
    return count > 0 ? ((index + step) % count + count) % count : 0;
  }
  return { uniqueSlides, shuffle, prepare, nextIndex };
});
