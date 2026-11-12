document.addEventListener('DOMContentLoaded', () => {
  document.querySelectorAll('[data-confirm]').forEach(el=>el.addEventListener('click',e=>{
    if(!window.confirm(el.dataset.confirm||'¿Confirmar la acción?'))e.preventDefault();
  }));
  if(!window.CITICIO_BOOKING)return;
  const button=document.getElementById('btnCheck');
  const date=document.getElementById('Fecha');
  const hour=document.getElementById('Hora');
  const branch=document.getElementById('SucursalId');
  const service=document.getElementById('ServicioId');
  const professional=document.getElementById('EmpleadoId');
  const status=document.getElementById('availabilityStatus');
  if(!button||!date||!hour||!branch||!service||!professional)return;
  const clear=()=>{professional.innerHTML='<option value="">Consulta la disponibilidad primero</option>';};
  date.addEventListener('change',clear);hour.addEventListener('change',clear);
  button.addEventListener('click',async()=>{
    clear();
    if(!date.value||!hour.value){status.textContent='Selecciona fecha y hora.';return;}
    status.textContent='Buscando profesionales disponibles…';button.disabled=true;
    try{
      const url=new URL(button.dataset.url,window.location.href);
      url.searchParams.set('sucursalId',branch.value);url.searchParams.set('servicioId',service.value);
      url.searchParams.set('fecha',date.value);url.searchParams.set('hora',hour.value);
      const response=await fetch(url,{headers:{'Accept':'application/json'},credentials:'same-origin'});
      if(!response.ok)throw Error('No se pudo consultar');
      const items=await response.json();
      for(const x of items){const opt=document.createElement('option');opt.value=x.id;opt.textContent=`${x.nombre} · termina ${x.fin}`;professional.appendChild(opt);}
      status.textContent=items.length?`${items.length} profesional(es) disponible(s). Selecciona uno.`:'No hay profesionales disponibles. Intenta otro horario.';
    }catch(e){status.textContent='Ocurrió un problema al consultar el horario. Reintenta.';}
    finally{button.disabled=false;}
  });
});

// Validación previa al envío. El backend vuelve a verificar bytes, tipo y tamaño.
document.querySelectorAll('input[type="file"][data-max-image]').forEach(input => {
  input.addEventListener('change', () => {
    input.setCustomValidity('');
    const file = input.files && input.files[0];
    if (!file) return;
    if (file.size > 2097152 || file.size === 0)
      input.setCustomValidity('Selecciona una imagen de hasta 2 MB (2,097,152 bytes).');
    else if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type))
      input.setCustomValidity('Solo se permiten JPG, PNG y WebP.');
    input.reportValidity();
  });
});
