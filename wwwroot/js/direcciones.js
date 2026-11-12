/* CITICIO — selectores en cascada; carga mediante API alimentada por SP */
(() => {
  const get = async (url) => {
    const r = await fetch(url, {headers: {Accept:'application/json'}});
    if (!r.ok) throw new Error('No se pudo consultar el catálogo geográfico. Comprueba los SP y la importación nacional.');
    return r.json();
  };
  const llenar = (select, rows, placeholder, formatter) => {
    select.replaceChildren(new Option(placeholder, ''));
    rows.forEach(x => select.add(new Option(formatter(x), String(x.id))));
    select.disabled = !rows.length;
  };
  document.querySelectorAll('[data-direccion]').forEach(async grupo => {
    const estado = grupo.querySelector('[data-estado]');
    const municipio = grupo.querySelector('[data-municipio]');
    const colonia = grupo.querySelector('[data-colonia]');
    const cp = grupo.querySelector('[data-cp]');
    const aviso = document.createElement('small');
    aviso.className = 'muted';
    aviso.setAttribute('role','status');
    grupo.append(aviso);
    const resetColonia = () => llenar(colonia, [], 'Selecciona municipio', x => x.nombre);
    const cargarColonias = async () => {
      resetColonia();
      if (!municipio.value) return;
      const code = cp.value.trim();
      if (code && !/^\d{5}$/.test(code)) { aviso.textContent='El código postal debe tener cinco dígitos.'; return; }
      aviso.textContent = 'Buscando colonias...';
      const q = `/Geografia/Colonias?municipioId=${encodeURIComponent(municipio.value)}${code ? `&cp=${encodeURIComponent(code)}` : ''}`;
      const rows = await get(q);
      llenar(colonia, rows, rows.length ? 'Elige colonia' : 'No hay colonias para esa búsqueda', x => `${x.nombre} (${x.cp})`);
      aviso.textContent = rows.length === 500 ? 'Se muestran las primeras 500. Escribe el código postal para filtrar.' : `${rows.length} asentamiento(s) encontrados.`;
    };
    try {
      llenar(estado, await get('/Geografia/Estados'), 'Selecciona tu estado', x => x.nombre);
      aviso.textContent='Selecciona un estado para continuar.';
    } catch(e) { aviso.textContent = e.message; return; }
    const cargarMunicipios = async () => {
      llenar(municipio, [], 'Cargando municipios...', x=>x.nombre);
      resetColonia(); cp.value='';
      if (!estado.value) return;
      try {
        llenar(municipio, await get(`/Geografia/Municipios?estadoId=${encodeURIComponent(estado.value)}`), 'Selecciona municipio', x=>x.nombre);
      } catch(e) { aviso.textContent=e.message; }
    };
    estado.addEventListener('change', () => cargarMunicipios().catch(e=>aviso.textContent=e.message));
    municipio.addEventListener('change', () => cargarColonias().catch(e=>aviso.textContent=e.message));
    cp.addEventListener('change', () => cargarColonias().catch(e=>aviso.textContent=e.message));
    // Edición: mantener la entidad, municipio y colonia existentes.
    if(grupo.dataset.initialEstado){
      estado.value = grupo.dataset.initialEstado;
      await cargarMunicipios();
      municipio.value = grupo.dataset.initialMunicipio || '';
      await cargarColonias();
      colonia.value = grupo.dataset.initialColonia || '';
    }
  });
})();
