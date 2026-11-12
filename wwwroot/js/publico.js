'use strict';
(() => {
  const grid = document.querySelector('#resultsGrid');
  const status = document.querySelector('#resultsStatus');
  if (!grid || !status) return;
  const top = grid.hasAttribute('data-top10');
  const qs = new URLSearchParams(location.search);
  const texto = qs.get('texto')?.trim() || '';
  const input = document.querySelector('#catalogoTexto');
  if (input) input.value = texto;
  const money = value => Number(value ?? 0).toLocaleString('es-MX', {style: 'currency', currency: 'MXN'});
  const add = (tag, cls, parent, value) => {const el=document.createElement(tag);if(cls)el.className=cls;if(value!=null)el.textContent=String(value);parent.appendChild(el);return el;};
  async function load(){
    try {
      const url = top ? '/api/publico/top10' : `/api/publico/catalogo?${new URLSearchParams({ texto })}`;
      const response = await fetch(url, {headers:{Accept:'application/json'}});
      const data = await response.json();
      if (!response.ok) throw Error(data.error || 'No fue posible consultar los resultados.');
      if (!Array.isArray(data)) throw Error('La respuesta del servidor no tiene el formato esperado.');
      grid.replaceChildren();
      if (!data.length){status.textContent=top?'Aún no existen empresas con estadísticas suficientes para aparecer en Top 10.':'No encontramos resultados. Prueba con otra búsqueda.';return;}
      status.hidden=true;
      data.forEach((item, index)=>{
        const empresaId=Number(item.id_empresa||0), servicioId=Number(item.id_servicio||0), sucursalId=Number(item.id_sucursal||0);
        const card=add('article','result-card',grid);
        const visual=add('div','result-photo',card);
        const img=document.createElement('img'); img.loading='lazy';
        img.alt=(top?'Logotipo de ':'Imagen del servicio en ')+String(item.empresa||'la empresa');
        img.src=top ? `/Imagenes/Ver?uso=logo_empresa&id=${empresaId}` : `/Imagenes/Ver?uso=imagen_servicio&id=${servicioId}`;
        img.addEventListener('error',()=>{img.remove();visual.classList.add('no-image');},{once:true});
        visual.appendChild(img);
        const label=add('span','tag',card,top?`#${index+1} · Empresa destacada`:(item.empresa||'Empresa'));
        const h=add('h3','',card);
        const link=add('a','',h,top?(item.empresa||'Empresa'):(item.servicio||'Servicio'));
        link.href=top?`/empresa/${empresaId}`:`/servicio/${sucursalId}/${servicioId}`;
        if(top){
          add('p','',card,`${Number(item.sucursales||0)} sucursales · ${Number(item.citas_completadas||0)} citas completadas`);
          add('p','',card,`★ ${item.calificacion_promedio==null?'Sin reseñas':`${item.calificacion_promedio}/5 · ${Number(item.cantidad_resenas||0)} reseñas`}`);
        } else {
          add('p','',card,`${item.sucursal||'Sucursal'} · ${item.municipio||'México'}`);
          add('strong','price',card,money(item.precio));
          add('p','',card,`Duración: ${item.duracion_minutos||'Por confirmar'} minutos`);
        }
        const actions=add('div','result-actions',card);
        const perfil=add('a','btn btn-outline btn-sm',actions,'Ver empresa');perfil.href=`/empresa/${empresaId}`;
        if(!top && servicioId>0 && sucursalId>0){
          const detalles=add('a','btn btn-outline btn-sm',actions,'Ver servicio');
          detalles.href=`/servicio/${sucursalId}/${servicioId}`;
          const reservar=add('a','btn btn-primary btn-sm',actions,'Agendar cita');
          reservar.href=`/Citas/Nueva?servicioId=${servicioId}&sucursalId=${sucursalId}`;
          const token=document.querySelector('#favoritosToken input[name="__RequestVerificationToken"]');
          if(token){
            const form=add('form','favorite-inline',actions);
            form.method='post';form.action='/Cliente/Favorito';
            const security=document.createElement('input');security.type='hidden';security.name='__RequestVerificationToken';security.value=token.value;form.appendChild(security);
            const field=document.createElement('input');field.type='hidden';field.name='servicioId';field.value=String(servicioId);form.appendChild(field);
            const like=add('button','btn btn-outline btn-sm',form,'♡ Favorito');like.type='submit';
          }
        }
      });
    }catch(err){status.hidden=false;status.textContent=err.message||'No fue posible consultar los resultados.';}
  }
  load();
})();
