'use strict';
(() => {
  const $ = (q,base=document)=>base.querySelector(q);
  const root=$('#app'), modal=$('#modal-root');
  const state = { route:'inicio', items:[], top:[], giros:[], estados:[], municipios:[],
    user:null, fav:new Set(), citas:[], pagadas:new Set(), pagos:[], resenas:[], perfil:null, filters:{texto:'',giro:'',estado:'',municipio:''},
    heroIndex:0, slides:[], booking:null, employees:[], selectedEmployee:0, csrf:null, error:null };
  let toastTimer;
  const esc = v => String(v ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const money = n => new Intl.NumberFormat('es-MX',{style:'currency',currency:'MXN',maximumFractionDigits:0}).format(Number(n)||0);
  const dateStr = x => {if(!x)return '';const d=new Date(x);return Number.isNaN(d.valueOf()) ? String(x).slice(0,10) : new Intl.DateTimeFormat('es-MX',{dateStyle:'long'}).format(d);};
  const url = v => {const t=String(v??'').trim(); if(/^\/(?!\/)/.test(t)) return esc(t);
    try {const u=new URL(t);return u.protocol==='https:'?esc(u.href):'';}catch{return '';}};
  const icon = s => {const t=(s?.giro||s?.servicio||s?.nombre_giro||'').toLowerCase();
    if(/odont|dental|dentista/.test(t))return '🦷';if(/veter|animal/.test(t))return '🐾';
    if(/gimnasio|deporte|fit/.test(t))return '🏋️';if(/tecno|reparaci/.test(t))return '💻';
    if(/foto/.test(t))return '📸';if(/mecá|auto/.test(t))return '🚗';
    if(/salud|médico/.test(t))return '🩺';if(/bienestar|fisioter|psicolo/.test(t))return '🌿';
    if(/belleza|barber|peinado|corte|manicure/.test(t))return '✂️';return '✳️';};
  const notify=(msg,bad=false)=>{const box=$('#toast');clearTimeout(toastTimer);box.textContent=msg;box.className='visible'+(bad?' error':'');toastTimer=setTimeout(()=>box.className='',4500);};
  async function api(path, opts={}) {
    const method=(opts.method||'GET').toUpperCase();
    const headers={'Accept':'application/json',...opts.headers};
    if(!['GET','HEAD'].includes(method)) {
      if(!state.csrf)state.csrf=(await api('/api/auth/csrf')).token;
      headers['X-CSRF-TOKEN']=state.csrf;
      headers['Content-Type']='application/json';
    }
    let response;
    try{response=await fetch(path,{...opts,headers,credentials:'same-origin'});}catch {throw Object.assign(new Error('No se puede conectar con el servidor CITICIO.'),{status:503});}
    let data;
    try {data=await response.json();}catch {data={};}
    if(!response.ok && response.status===400 && data.code==='csrf_invalid' && !opts._csrfReintento) {
      state.csrf=null;
      return api(path,{...opts,_csrfReintento:true});
    }
    if(!response.ok)throw Object.assign(new Error(data.error||data.title||(response.status===401?'Inicia sesión para continuar.':`Error HTTP ${response.status}`)),{status:response.status});
    return data;
  }
  const call=(path,method,body)=>api(path,{method,body:body?JSON.stringify(body):undefined});
  const qs = obj => {const p=new URLSearchParams();Object.entries(obj).forEach(([k,v])=>{if(v!==''&&v!=null)p.set(k,v);});return p.toString();};
  const imageOf = (item,mode='servicio') => {
    const id=Number(mode==='empresa'?item.id_empresa:item.id_servicio);
    if(id>0)return `/Imagenes/Ver?uso=${mode==='empresa'?'portada_empresa':'imagen_servicio'}&id=${id}`;
    return url(item.imagen_servicio||item.imagen_portada_url||item.imagen_sucursal||item.imagen_url||item.logo_url);
  };
  const bannerImage=(item)=>{const src=imageOf(item,'empresa');return src?`<img src="${src}" alt="Imagen de ${esc(item.empresa)}" onerror="this.remove()">`:'';};
  const onePer = (list,key) => [...new Map(list.map(i=>[i[key],i])).values()];
  const homeSlides=()=>state.slides;
  function shuffleHero() {
    const previous=sessionStorage.getItem('citicio_last_hero');
    state.slides=window.CiticioCarousel.prepare(state.items,previous);
    state.heroIndex=0;
    if(state.slides.length)sessionStorage.setItem('citicio_last_hero',String(state.slides[0].id_empresa));
  }
  function card(item, mode='servicio') {
    const empresaMode=mode==='empresa';
    const title=empresaMode?item.empresa:item.servicio;
    const subtitle=empresaMode?item.sucursal:item.empresa;
    const cover = imageOf(item,empresaMode?'empresa':'servicio');
    return `<article class="listing-card" data-test="card">
      <div class="card-cover"><span class="cover-emoji" aria-hidden="true">${icon(item)}</span>
        ${cover?`<img src="${cover}" alt="${esc(title)}" loading="lazy" onerror="this.remove()">`:''}
        <span class="chip">${esc(empresaMode?'Establecimiento':item.sucursal)}</span>
        ${!empresaMode?`<button class="favorite-heart ${state.fav.has(item.id_servicio)?'on':''}" data-action="favorite" data-id="${item.id_servicio}" aria-label="${state.fav.has(item.id_servicio)?'Quitar de':'Agregar a'} favoritos" title="Favoritos">${state.fav.has(item.id_servicio)?'♥':'♡'}</button>`:''}
      </div>
      <div class="card-body"><div class="card-meta"><span>⌖ ${esc(item.municipio||'México')}</span><span>${Number(item.duracion_minutos||0)} min</span></div>
        <h3>${esc(title)}</h3><p>${esc(empresaMode?`Encuentra ${item.servicio} y más servicios disponibles. Decídete por el horario ideal.`:item.descripcion||`Servicio de ${subtitle} con reservación.`)}</p>
        <a class="btn btn-outline small" href="/empresa/${Number(item.id_empresa)||0}">Perfil, sucursales y reseñas →</a>
        <div class="card-bottom"><div><div class="price">${money(item.precio)}<small>Precio por servicio</small></div></div>
          <button class="btn btn-primary small" data-action="book" data-id="${item.id_servicio}" data-branch="${item.id_sucursal}">Agendar <span aria-hidden="true">↗</span></button></div>
      </div>
    </article>`;
  }
  const sectionHead=(tiny,title,sub,more='',route='servicios')=>`<div class="intro-row"><div><div class="section-label">${esc(tiny)}</div><h2>${esc(title)}</h2><div class="sub">${esc(sub)}</div></div>${more?`<a class="link-more" data-route="${route}" href="#${route}">${esc(more)} →</a>`:''}</div>`;
  function categories() {
    const featured=['Belleza y estética','Salud','Bienestar','Odontología','Deporte y gimnasio','Veterinaria'];
    const data=[...state.giros].sort((a,b)=>{
      const rank=x=>{const found=featured.findIndex(v=>x.nombre_giro.toLocaleLowerCase('es').includes(v.toLocaleLowerCase('es')));return found<0?99:found;};
      return rank(a)-rank(b);
    }).slice(0,6);
    if(!data.length)return '';
    return `${sectionHead('Explora a tu manera','¿Qué necesitas hoy?','Elige una categoría y encuentra el servicio adecuado.')}
    <div class="category-grid">${data.slice(0,6).map(g=>`<button class="category-tile" data-action="category" data-id="${g.id_giro}">
      <span class="category-icon" aria-hidden="true">${icon(g)}</span><b>${esc(g.nombre_giro)}</b></button>`).join('')}</div>`;
  }
  function slideMarkup(slide,index,total){return `<div class="hero-art" id="hero-art">${slide?bannerImage(slide):''}
      <div class="hero-arrows"><button class="hero-arrow" data-action="prevSlide" aria-label="Imagen anterior">‹</button><button class="hero-arrow" data-action="nextSlide" aria-label="Imagen siguiente">›</button></div>
      <div class="hero-business"><small>✦ ${slide?'DESCUBRE EN CITICIO':'BUSCA TUS SERVICIOS'}</small>
      <strong>${esc(slide?.empresa||'Un mundo de experiencias')}</strong>
      <span>⌖ ${esc(slide?.municipio||'En tu ciudad')} · ${esc(slide?.servicio||'Encuentra tu próxima cita')}</span></div>
      <div class="hero-pagination">${Array.from({length:Math.max(1,total)},(_,i)=>`<button class="hero-dot ${i===index?'active':''}" data-action="dotSlide" data-index="${i}" aria-label="Mostrar imagen ${i+1}"></button>`).join('')}</div>
    </div>`;}
  function home() {
    const slides=homeSlides();const slide=slides[state.heroIndex%Math.max(1,slides.length)];
    return `<section class="hero"><div class="hero-content"><div class="eyebrow">✳ TU TIEMPO IMPORTA</div>
      <h1>Tu próxima cita,<br><em>a un clic.</em></h1><p>Explora negocios cerca de ti, descubre sus servicios y reserva en el momento que mejor te venga.</p>
      <form id="hero-search" class="hero-search"><span class="magnify" aria-hidden="true">⌕</span>
        <input name="texto" type="search" placeholder="¿Qué servicio estás buscando?" aria-label="Buscar servicio" maxlength="150">
        <button class="btn btn-primary" type="submit">Buscar</button></form></div>${slideMarkup(slide,state.heroIndex,slides.length)}</section>
      ${categories()}
      ${sectionHead('Conoce lugares increíbles','Servicios para ti','Opciones disponibles en los negocios registrados.','Ver todos los servicios','servicios')}
      ${state.items.length?`<div class="card-grid">${state.items.slice(0,6).map(c=>card(c)).join('')}</div>`:empty('No hay servicios disponibles','Los negocios aparecerán aquí cuando estén activos y publiquen servicios.')}
      ${sectionHead('Comunidad CITICIO','Establecimientos destacados','Explora empresas que puedes descubrir y reservar.','Ver empresas','empresas')}
      ${state.items.length?`<div class="card-grid">${onePer(state.items,'id_empresa').slice(0,3).map(c=>card(c,'empresa')).join('')}</div>`:''}
      <div class="banner"><div><div class="banner-title">✨ Un lugar para todas tus citas</div><p>Conoce opciones de belleza, salud, bienestar, tecnología y más.</p></div>
        <button class="btn btn-primary" data-route="top10">Explorar Top 10 →</button></div>`;
  }
  function empty(title,desc,action='',route='servicios') {return `<div class="empty"><span class="empty-emoji">🗓️</span><h3>${esc(title)}</h3><p>${esc(desc)}</p>${action?`<button class="btn btn-primary" data-route="${route}">${esc(action)}</button>`:''}</div>`;}
  function filters() {
    return `<form id="filters" class="filter-bar"><input id="f-search" type="search" name="texto" placeholder="Buscar empresa o servicio" value="${esc(state.filters.texto)}" maxlength="150">
    <select name="giro" aria-label="Giro"><option value="">Todas las categorías</option>${state.giros.map(g=>`<option value="${g.id_giro}" ${state.filters.giro==g.id_giro?'selected':''}>${esc(g.nombre_giro)}</option>`).join('')}</select>
    <select name="estado" aria-label="Estado"><option value="">Todo México</option>${state.estados.map(g=>`<option value="${g.id}" ${state.filters.estado==g.id?'selected':''}>${esc(g.nombre)}</option>`).join('')}</select>
    <select name="municipio" aria-label="Municipio"><option value="">Todos los municipios</option>${state.municipios.map(g=>`<option value="${g.id}" ${state.filters.municipio==g.id?'selected':''}>${esc(g.nombre)}</option>`).join('')}</select>
    <button type="submit" class="btn btn-primary">Filtrar</button></form>`;
  }
  function catalogPage(mode) {
    const enterprise=mode==='empresas';let data=state.filtered??state.items;if(enterprise)data=onePer(data,'id_empresa');
    return `<div class="page-head"><div class="breadcrumb">Inicio / ${enterprise?'Empresas':'Servicios'}</div>
      <h1>${enterprise?'Empresas que inspiran':'Encuentra tu siguiente servicio'}</h1><p class="sub">${enterprise?'Descubre establecimientos registrados y sus servicios.':'Elige un servicio, consulta la disponibilidad y agenda tu cita.'}</p></div>
      ${filters()}<p class="sub" style="margin-bottom:19px">${data.length} ${enterprise?'empresas':'servicios'} encontrados</p>
      ${data.length?`<div class="card-grid">${data.map(i=>card(i,enterprise?'empresa':'servicio')).join('')}</div>`:empty('No encontramos resultados','Prueba otra búsqueda, categoría o ubicación.')}`;
  }
  function topPage(){return `<div class="page-head"><div class="breadcrumb">Inicio / Top 10</div><h1>Top 10 de empresas</h1>
    <p class="sub">Ranking real según citas completadas, reseñas y calificaciones. Sin posiciones inventadas.</p></div>
    ${state.top.length?`<div class="card-grid">${state.top.map((t,i)=>`<article class="listing-card">
      <div class="card-cover"><span class="cover-emoji">🏅</span><img src="/Imagenes/Ver?uso=logo_empresa&id=${Number(t.id_empresa)||0}" alt="${esc(t.empresa)}" onerror="this.remove()"><span class="chip">#${i+1} · Top 10</span></div>
      <div class="card-body"><div class="card-meta"><span>${Number(t.sucursales)||0} sucursales</span><span class="rating">${t.calificacion_promedio==null?'Sin calificaciones':`★ ${Number(t.calificacion_promedio).toFixed(1)}`}</span></div>
      <h3>${esc(t.empresa)}</h3><p>${Number(t.citas_completadas)||0} citas completadas · ${Number(t.cantidad_resenas)||0} reseñas</p>
      <div class="card-bottom"><span class="rank">Puesto ${i+1}</span><a class="btn btn-outline small" href="/empresa/${Number(t.id_empresa)||0}">Ver empresa →</a></div></div></article>`).join('')}</div>`
      :empty('Aún no hay clasificación','El ranking se genera con las citas y calificaciones reales de CITICIO.')}`;}
  const stateLabel=s=>({cita_pendiente:'Pendiente',cita_confirmada:'Confirmada',cita_cancelada:'Cancelada',cita_completada:'Completada'}[s]||s);
  function appointmentsPage(){return `<div class="page-head"><div class="breadcrumb">Mi cuenta / Citas</div><h1>Mis citas</h1><p class="sub">Consulta y administra tus próximas reservaciones.</p><div class="account-links"><a href="#pagos" data-route="pagos">Ver mis pagos →</a><a href="#resenas" data-route="resenas">Ver mis reseñas →</a></div></div>
  ${!state.user?empty('Accede a tus citas','Inicia sesión para ver todas tus reservas.')+`<button class="btn btn-primary" data-action="login">Iniciar sesión</button>`:
    !state.citas.length?empty('Aún no tienes citas','Explora servicios y haz tu primera reservación.','Buscar servicios'):
    `<div class="booking-list">${state.citas.map(c=>{const pagada=state.pagadas.has(Number(c.id_cita));return `<article class="booking-item"><div><h3>${esc(c.empresa)} · ${esc(c.sucursal)}</h3>
      <p>📅 ${esc(dateStr(c.hora_inicio))} · ${esc(String(c.hora_inicio||'').slice(11,16))} h</p>
      <p>👤 ${esc(c.empleado)} · Total: <b>${money(c.total_cita)}</b></p></div>
      <div class="booking-actions"><span class="status ${esc((c.nombre_estado||'').replace('cita_',''))}">${esc(stateLabel(c.nombre_estado))}</span>
      ${pagada?`<span class="status confirmada">Pago confirmado</span><a class="btn btn-outline small" href="/Citas/Comprobante/${Number(c.id_cita)}">Ticket PDF + QR</a>`:''}
      ${!pagada && ['cita_pendiente','cita_confirmada'].includes(c.nombre_estado) && Number(c.total_cita)>0 ?
        `<button class="btn btn-primary small" data-action="pagoTarjeta" data-id="${Number(c.id_cita)}">Pagar con tarjeta · CITICIO Citas</button>
         <button class="btn btn-outline small" data-action="pagoSpei" data-id="${Number(c.id_cita)}">Transferencia SPEI</button>`:''}
      ${['cita_pendiente','cita_confirmada'].includes(c.nombre_estado)&&new Date(c.hora_inicio)>new Date()?`<button class="btn btn-danger small" data-action="cancel" data-id="${c.id_cita}">Cancelar</button>`:''}
      ${c.nombre_estado==='cita_completada'&&!state.resenas.some(r=>r.id_cita===c.id_cita)?`<button class="btn btn-outline small" data-action="review" data-id="${c.id_cita}">Dejar reseña ★</button>`:''}</div></article>`;}).join('')}</div>`}`;}
  function favPage(){const items=state.items.filter(x=>state.fav.has(x.id_servicio));return `<div class="page-head"><div class="breadcrumb">Mi cuenta / Favoritos</div><h1>Mis favoritos ♥</h1><p class="sub">Tus servicios guardados, disponibles cuando los necesites.</p></div>
  ${!state.user?empty('Guarda lo que más te gusta','Inicia sesión para crear tu lista de favoritos.')+`<button class="btn btn-primary" data-action="login">Iniciar sesión</button>`:
  items.length?`<div class="card-grid">${onePer(items,'id_servicio').map(c=>card(c)).join('')}</div>`:empty('No hay favoritos todavía','Pulsa el corazón de cualquier servicio para guardarlo.','Explorar servicios')}`;}
  function paymentsPage(){return `<div class="page-head"><div class="breadcrumb">Mi cuenta / Pagos</div><h1>Mis pagos</h1><p class="sub">Movimientos registrados por la pasarela. Esta pantalla no realiza cargos por sí sola.</p></div>
    ${!state.user?empty('Inicia sesión','Consulta tus pagos de manera segura.'):
      !state.pagos.length?empty('Aún no hay movimientos','Aquí aparecerán los pagos confirmados o pendientes de tus citas.'):
      `<div class="booking-list">${state.pagos.map(p=>`<div class="booking-item"><div><h3>${esc(p.empresa)} · ${esc(p.sucursal)}</h3>
      <p>Referencia: ${esc(p.referencia||'Pendiente')} · ${esc(dateStr(p.fecha_pago))} · ${esc(p.nombre_metodo||'')}</p></div>
      <div class="booking-actions"><span class="status ${esc((p.nombre_estado||'').replace('pago_',''))}">${esc(p.nombre_estado)}</span><strong class="price">${money(p.monto)}</strong></div></div>`).join('')}</div>`}`;}
  function reviewsPage(){return `<div class="page-head"><div class="breadcrumb">Mi cuenta / Reseñas</div><h1>Mis reseñas</h1><p class="sub">Las experiencias que has compartido con la comunidad.</p></div>
    ${!state.user?empty('Inicia sesión','Consulta tus opiniones.'):
      !state.resenas.length?empty('Todavía no has opinado','Al completar una cita podrás valorar al establecimiento.'):
      `<div class="booking-list">${state.resenas.map(r=>`<div class="booking-item"><div><h3>Reseña de tu cita #${Number(r.id_cita)}</h3>
      <p>★ ${Number(r.calificacion_sucursal||r.calificacion_empresa)}/5 · ${esc(r.sucursal||'')} · ${esc(dateStr(r.fecha_resena))}</p><p>${esc(r.comentario||'Sin comentario')}</p></div></div>`).join('')}</div>`}`;}
  function profilePage(){const p=state.perfil;if(!state.user)return empty('Accede a tu perfil','Inicia sesión para ver tus datos.');
    if(!p)return '<div class="loading-block">Cargando perfil…</div>';
    return `<div class="page-head"><div class="breadcrumb">Mi cuenta / Perfil</div><h1>Mi perfil</h1><p class="sub">Actualiza tus datos de contacto y preferencias.</p></div>
      <div class="simple-panel" style="max-width:700px"><form id="profile-form" class="modal-form"><div class="form-grid">
      ${field('Nombre','nombre',p.nombre,100)}${field('Apellido paterno','apellidoPaterno',p.apellido_paterno,100)}
      ${field('Apellido materno','apellidoMaterno',p.apellido_materno,100)}${field('Teléfono','telefono',p.telefono,15,'tel')}
      <label class="form-full"><span class="field-label">Correo de acceso</span><input class="field" value="${esc(p.correo)}" disabled><small class="helper">El correo no se cambia aquí para mantener sincronizado el inicio de sesión.</small></label>
      <label class="form-full checkbox-row"><input type="checkbox" name="aceptaWhatsapp" ${p.acepta_whatsapp?'checked':''}> Deseo recibir notificaciones por WhatsApp.</label>
      ${field('WhatsApp (+52...)','telefonoWhatsapp',p.telefono_whatsapp||'',16,'tel','form-full')}
      </div><button class="btn btn-primary" type="submit">Guardar mis datos</button></form></div>`;
  }
  function field(label,name,value='',max=150,type='text',cls=''){return `<label class="${cls}"><span class="field-label">${esc(label)}</span><input class="field" name="${name}" type="${type}" value="${esc(value)}" maxlength="${max}" ${name==='telefonoWhatsapp'?'':'required'}></label>`;}
  function render() {
    state.route=(location.hash.replace('#','')||'inicio').split('?')[0];
    if(!['inicio','empresas','servicios','top10','citas','favoritos','perfil','pagos','resenas'].includes(state.route))state.route='inicio';
    const views={inicio:home,empresas:()=>catalogPage('empresas'),servicios:()=>catalogPage('servicios'),top10:topPage,citas:appointmentsPage,favoritos:favPage,perfil:profilePage,pagos:paymentsPage,resenas:reviewsPage};
    root.innerHTML= state.error ? empty('No se pudo cargar CITICIO',state.error)+`<div style="text-align:center"><button class="btn btn-primary" data-action="retry">Volver a intentar</button></div>` : views[state.route]();
    document.querySelectorAll('.nav-item').forEach(a=>a.classList.toggle('active',a.dataset.route===state.route));
    const auth=$('#account-area');
    auth.innerHTML=state.user?`<button class="btn btn-ghost small" data-route="favoritos" aria-label="Ver favoritos">♥</button><button class="btn btn-outline small" data-route="perfil">Hola, ${esc(state.user.nombre||'Cliente').split(' ')[0]}</button><button class="btn btn-ghost small" data-action="logout">Salir</button>`
      :`<button class="btn btn-ghost" data-action="login">Iniciar sesión</button><button class="btn btn-primary small" data-action="registro">Crear cuenta ↗</button>`;
  }
  function tickHero(direction=1){if(state.route!=='inicio'||state.error)return;const slides=homeSlides();if(slides.length<=1)return;
    state.heroIndex=window.CiticioCarousel.nextIndex(state.heroIndex,slides.length,direction);const original=$('#hero-art');
    if(original)original.outerHTML=slideMarkup(slides[state.heroIndex],state.heroIndex,slides.length);
  }
  async function refreshCatalog() {
    state.items=await api('/api/catalogo');shuffleHero();state.top=await api('/api/top10');state.giros=await api('/api/giros');
    state.estados=await api('/api/estados');
    if(state.user)await refreshAuth();state.error=null;render();
  }
  async function refreshAuth(){if(!state.user)return;
    const [f,c,p,r,pagadas]=await Promise.all([api('/api/cliente/favoritos'),api('/api/cliente/citas'),api('/api/cliente/pagos'),api('/api/cliente/resenas'),api('/api/cliente/citas/estados-pago')]);
    state.fav=new Set(f.map(x=>x.id_servicio));state.citas=c;state.pagos=p;state.resenas=r;
    state.pagadas=new Set(pagadas.map(Number));
  }
  async function loginRefresh(){try{state.user=await api('/api/auth/me');await refreshAuth();}
    catch(e){if(e.status!==401)throw e;state.user=null;}}
  async function init(){ $('#year').textContent=new Date().getFullYear();
    try{await loginRefresh();await refreshCatalog();}catch(e){state.error=e.message;render();}
    const estadoCheckout=new URLSearchParams(location.search).get('checkout');
    if(estadoCheckout==='verificando') {
      const cita=Number(new URLSearchParams(location.search).get('citaId'));
      if(cita>0 && state.pagadas.has(cita))notify('Pago confirmado. CITICIO enviará el correo de confirmación.');
      else {
        notify('Verificando el pago con Stripe, no vuelvas a pagar...');
        let revisiones=0;
        const timer=setInterval(async()=>{
          if(++revisiones>20 || !state.user) {clearInterval(timer);return;}
          try {await refreshAuth();render();if(state.pagadas.has(cita)) {
            clearInterval(timer);notify('Pago confirmado. CITICIO enviará el correo y el ticket PDF + QR.');
          }} catch {/* Reintenta sin solicitar otro cargo. */}
        },1200);
      }
    }
    else if(estadoCheckout==='cancelado')notify('No se completó el pago con tarjeta.');
    if(estadoCheckout && window.history?.replaceState)history.replaceState(null,'',location.pathname+location.hash);
    setInterval(()=>{if(!document.hidden)tickHero(1);},6500);
    setInterval(async()=>{if(document.hidden)return;try{state.items=await api('/api/catalogo');shuffleHero();if(state.route==='inicio'){const node=$('#hero-art');if(node)node.outerHTML=slideMarkup(state.slides[0],0,state.slides.length);}}catch{/* Próxima actualización automática. */}},300000);
  }
  async function navigate(page){const next=page||'inicio';location.hash=next;if(next==='citas'&&state.user){try{await refreshAuth();}catch(e){notify(e.message,true);}}
    if(next==='perfil'&&state.user){try{state.perfil=await api('/api/cliente/perfil');}catch(e){notify(e.message,true);}}
    if(location.hash==='#'+next)render();window.scrollTo({top:0,behavior:'smooth'});}
  function showModal(title,body,footer=''){modal.innerHTML=`<div class="modal-backdrop" data-action="backdrop"><div class="modal" role="dialog" aria-modal="true" aria-label="${esc(title)}"><div class="modal-head"><div><div class="section-label">CITICIO</div><h2>${esc(title)}</h2></div><button class="close" data-action="close" aria-label="Cerrar ventana">×</button></div>${body}${footer}</div></div>`;
    $('.modal .field')?.focus();document.body.style.overflow='hidden';}
  function closeModal(){modal.innerHTML='';document.body.style.overflow='';}
  function authModal(type='login'){if(type==='login')showModal('Bienvenido de nuevo',`<form id="login-form" class="modal-form">
      ${field('Correo electrónico','correo','',254,'email')}${field('Contraseña','contrasena','',100,'password')}
      <button class="btn btn-primary" type="submit">Iniciar sesión →</button></form>`,
      `<div class="modal-footer"><a href="/Seguridad/Recuperar">¿Olvidaste tu contraseña?</a> · ¿No tienes cuenta? <button class="text-button" data-action="registro">Regístrate gratis</button></div>`);
    else showModal('Crea tu cuenta',`<form id="signup-form" class="modal-form"><div class="form-grid">
      ${field('Nombre(s)','nombre','',100)}${field('Apellido paterno','apellidoPaterno','',100)}
      ${field('Apellido materno','apellidoMaterno','',100)}
      <label><span class="field-label">País / lada telefónica</span><select class="field" name="ladaPais"><option value="+52">México (+52)</option><option value="+1">EE. UU./Canadá (+1)</option><option value="+57">Colombia (+57)</option><option value="+54">Argentina (+54)</option><option value="+34">España (+34)</option><option value="+51">Perú (+51)</option></select></label>
      ${field('Teléfono','telefono','',15,'tel')}
      ${field('Correo electrónico','correo','',254,'email','form-full')}
      <label><span class="field-label">Fecha de nacimiento</span><input class="field" name="fechaNacimiento" type="date" max="${new Date().toISOString().slice(0,10)}" required></label>
      ${field('Contraseña (8+ caracteres y combinación segura)','contrasena','',100,'password')}
      <label class="form-full checkbox-row"><input type="checkbox" name="aceptaWhatsapp"> Acepto recibir notificaciones por WhatsApp.</label>
      ${field('WhatsApp (opcional; utiliza la lada seleccionada)','telefonoWhatsapp','',16,'tel','form-full')}
      </div><button class="btn btn-primary" type="submit">Crear cuenta →</button></form>`,
      `<div class="modal-footer">¿Ya tienes cuenta? <button class="text-button" data-action="login">Inicia sesión</button></div>`);
  }
  function bookingMarkup(){const b=state.booking;const min=new Date();min.setDate(min.getDate()+1);const max=new Date();max.setDate(max.getDate()+60);
    const pad=d=>`${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
    return `<div class="summary-box"><b>${esc(b.servicio)}</b> · ${esc(b.empresa)}<br>
      <span>⌖ ${esc(b.sucursal)} · ${Number(b.duracion_minutos)} min · <b>${money(b.precio)}</b></span></div>
      <form id="availability-form" class="modal-form" style="margin-top:18px"><div class="form-grid">
      <label><span class="field-label">Fecha de la cita</span><input class="field" name="fecha" type="date" min="${pad(min)}" max="${pad(max)}" value="${esc(b.fecha||pad(min))}" required></label>
      <label><span class="field-label">Hora de inicio</span><input class="field" name="hora" type="time" value="${esc(b.hora||'10:00')}" step="1800" required></label>
      </div><button class="btn btn-outline" type="submit">Consultar profesionales disponibles</button></form>
      <div id="employees-container">${state.employees.length?`<div class="person-list"><p class="sub">Selecciona un profesional:</p>${state.employees.map(x=>`<button class="person-choice ${state.selectedEmployee===x.id_empleado?'selected':''}" data-action="person" data-id="${x.id_empleado}"><span>👤 ${esc(x.nombre)} ${esc(x.apellido_paterno)}</span><span>${state.selectedEmployee===x.id_empleado?'✓ Elegido':'Elegir →'}</span></button>`).join('')}</div><label style="display:block;margin-top:14px"><span class="field-label">Observaciones (opcional)</span><textarea class="field" id="booking-notes" rows="2" maxlength="1000">${esc(b.observaciones||'')}</textarea></label><button class="btn btn-primary" id="confirm-booking" data-action="confirmBook" ${state.selectedEmployee?'':'disabled'} style="width:100%;margin-top:13px">Confirmar mi cita</button>`:''}</div>`;
  }
  function openBooking(serviceId,branchId){if(!state.user){notify('Inicia sesión para poder reservar.');authModal();return;}
    const b=state.items.find(x=>x.id_servicio===serviceId&&x.id_sucursal===branchId);
    if(!b){notify('No encontramos el servicio.',true);return;}
    state.booking={...b,fecha:'',hora:'10:00',observaciones:''};state.employees=[];state.selectedEmployee=0;
    showModal('Agenda tu cita',bookingMarkup());
  }
  const formData=form=>Object.fromEntries(new FormData(form));
  async function doForm(e){if(!e.target.matches('#hero-search,#filters,#login-form,#signup-form,#availability-form,#profile-form,#review-form,#spei-form'))return;
    e.preventDefault();const f=e.target,d=formData(f),submit=$('[type="submit"]',f);if(submit)submit.disabled=true;
    try {
      if(f.id==='hero-search'){state.filters={texto:d.texto||'',giro:'',estado:'',municipio:''};state.filtered=await api('/api/catalogo?'+qs({texto:state.filters.texto}));await navigate('servicios');}
      if(f.id==='filters'){state.filters={texto:d.texto||'',giro:d.giro||'',estado:d.estado||'',municipio:d.municipio||''};
        state.filtered=await api('/api/catalogo?'+qs(state.filters));render();}
      if(f.id==='login-form'){const data=await call('/api/auth/login','POST',{correo:d.correo,contrasena:d.contrasena});
        state.user=data.usuario;state.csrf=null;await refreshAuth();closeModal();notify('¡Bienvenido a CITICIO!');render();}
      if(f.id==='signup-form'){
        if(!/^(?=.{8,128}$)(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^A-Za-z0-9\s]).*$/.test(d.contrasena))throw new Error('Usa 8+ caracteres, mayúscula, minúscula, número y símbolo.');
        if(!/^\+?\d{10,15}$/.test(d.telefono))throw new Error('Teléfono: introduce de 10 a 15 dígitos.');
        if(d.aceptaWhatsapp && !d.telefonoWhatsapp)d.telefonoWhatsapp=(d.ladaPais||'+52')+d.telefono.replace(/[^0-9]/g,'');
        if(d.aceptaWhatsapp && !/^\+[1-9]\d{8,14}$/.test(d.telefonoWhatsapp||''))throw new Error('WhatsApp debe tener formato internacional: +52...');
        const data=await call('/api/auth/registro','POST',{nombre:d.nombre,apellidoPaterno:d.apellidoPaterno,apellidoMaterno:d.apellidoMaterno,
          telefono:d.telefono,correo:d.correo,fechaNacimiento:d.fechaNacimiento,contrasena:d.contrasena,
          aceptaWhatsapp:!!d.aceptaWhatsapp,telefonoWhatsapp:d.aceptaWhatsapp?d.telefonoWhatsapp:null});
        state.user=data.usuario;state.csrf=null;await refreshAuth();closeModal();notify('¡Cuenta registrada!');render();}
      if(f.id==='availability-form'){
        state.booking.fecha=d.fecha;state.booking.hora=d.hora;state.selectedEmployee=0;
        state.employees=await api('/api/disponibilidad?'+qs({sucursal:state.booking.id_sucursal,servicio:state.booking.id_servicio,fecha:d.fecha,hora:d.hora}));
        showModal('Agenda tu cita',bookingMarkup());if(!state.employees.length)notify('No hay profesionales libres en ese horario. Prueba otro.',true);
      }
      if(f.id==='profile-form'){
        const data={nombre:d.nombre,apellidoPaterno:d.apellidoPaterno,apellidoMaterno:d.apellidoMaterno,telefono:d.telefono,
          aceptaWhatsapp:!!d.aceptaWhatsapp,telefonoWhatsapp:d.telefonoWhatsapp||null};
        await call('/api/cliente/perfil','PUT',data);state.perfil=await api('/api/cliente/perfil');notify('Perfil guardado.');render();}
      if(f.id==='spei-form'){
        const cita=Number(d.citaId);
        await call('/api/cliente/citas/'+cita+'/reportar-spei','POST',{
          idSucursal:Number(d.idSucursal),fechaOperacion:d.fechaOperacion,
          bancoEmisor:d.bancoEmisor,claveRastreo:d.claveRastreo,monto:Number(d.monto)
        });
        closeModal();notify('SPEI reportado. La sucursal verificará el depósito antes de confirmar el pago.');
      }
      if(f.id==='review-form'){
        await call('/api/cliente/resenas','POST',{cita:Number(d.cita),calificacion:Number(d.calificacion),comentario:d.comentario});
        closeModal();await refreshAuth();notify('¡Gracias por tu reseña!');render();}
    }catch(error){notify(error.message,true);}finally {if(submit&&document.contains(submit))submit.disabled=false;}
  }
  async function action(e){const el=e.target.closest('[data-action],[data-route]');if(!el)return;
    if(el.closest('#modal-root')&&el.dataset.action==='backdrop'&&e.target!==el)return;
    const act=el.dataset.action, route=el.dataset.route;
    if(route){e.preventDefault();$('.main-nav')?.classList.remove('open');await navigate(route);return;}
    if(!act)return;e.preventDefault();const id=Number(el.dataset.id);
    try {
      if(act==='close'||act==='backdrop'){closeModal();return;}
      if(act==='menu'){const nav=$('.main-nav');const open=nav.classList.toggle('open');el.setAttribute('aria-expanded',String(open));return;}
      if(act==='login'||act==='registro'){authModal(act);return;}
      if(act==='nextSlide'){tickHero(1);return;}if(act==='prevSlide'){tickHero(-1);return;}
      if(act==='dotSlide'){state.heroIndex=Number(el.dataset.index)-1;tickHero(1);return;}
      if(act==='retry'){state.error=null;await refreshCatalog();return;}
      if(act==='category'){state.filters={texto:'',giro:String(id),estado:'',municipio:''};state.filtered=await api('/api/catalogo?giro='+id);await navigate('servicios');return;}
      if(act==='openEnterprise'){state.filters={texto:'',giro:'',estado:'',municipio:''};state.filtered=state.items.filter(x=>x.id_empresa===id);await navigate('servicios');return;}
      if(act==='book'){openBooking(id,Number(el.dataset.branch));return;}
      if(act==='person'){state.selectedEmployee=id;const existing=$('#booking-notes');if(existing)state.booking.observaciones=existing.value;showModal('Agenda tu cita',bookingMarkup());return;}
      if(act==='confirmBook'){
        if(!state.selectedEmployee)return;
        state.booking.observaciones=$('#booking-notes')?.value||'';
        el.disabled=true;
        const saved=await call('/api/cliente/citas','POST',{sucursal:state.booking.id_sucursal,servicio:state.booking.id_servicio,
          empleado:state.selectedEmployee,fecha:state.booking.fecha,hora:state.booking.hora,observaciones:state.booking.observaciones});
        closeModal();await refreshAuth();notify(saved.mensaje||'Cita registrada');await navigate('citas');return;
      }
      if(act==='pagoTarjeta'){
        const cobro=await call('/api/cliente/citas/'+id+'/checkout','POST',{});
        if(!/^https:\/\/checkout\.stripe\.com\//.test(cobro?.url||''))throw Error('La pasarela no devolvió un enlace seguro.');
        location.href=cobro.url;return;
      }
      if(act==='pagoSpei'){
        const destino=await api('/api/cliente/citas/'+id+'/destino-spei');
        if(destino.saldo<=0){notify('Esta cita no tiene saldo pendiente.');return;}
        showModal('Transferencia SPEI a la sucursal',`
          <div class="summary-box"><strong>Banco:</strong> ${esc(destino.banco)}<br>
          <strong>Titular:</strong> ${esc(destino.titular)}<br>
          <strong>CLABE:</strong> ${esc(destino.clabe)}<br>
          <strong>Saldo pendiente:</strong> ${money(destino.saldo)} MXN</div>
          <p class="sub">Transfiere desde tu aplicación bancaria directamente a esta sucursal y después reporta el movimiento. La sucursal verificará el depósito.</p>
          <form id="spei-form" class="modal-form">
            <input type="hidden" name="citaId" value="${id}">
            <input type="hidden" name="idSucursal" value="${Number(destino.idSucursal)}">
            <label><span class="field-label">Fecha de transferencia</span><input class="field" type="date" name="fechaOperacion" required></label>
            <label><span class="field-label">Banco emisor (3 dígitos)</span><input class="field" name="bancoEmisor" pattern="[0-9]{3}" maxlength="3" required></label>
            <label><span class="field-label">Clave de rastreo SPEI</span><input class="field" name="claveRastreo" minlength="6" maxlength="100" required></label>
            <label><span class="field-label">Monto transferido</span><input class="field" name="monto" type="number" min="0.01" max="${Number(destino.saldo)}" step="0.01" value="${Number(destino.saldo)}" required></label>
            <button class="btn btn-primary" type="submit">Reportar transferencia</button>
          </form>`);return;
      }
      if(act==='favorite'){
        if(!state.user){authModal('login');return;}
        const had=state.fav.has(id);await call('/api/cliente/favoritos/'+id,had?'DELETE':'POST');
        if(had)state.fav.delete(id);else state.fav.add(id);notify(had?'Favorito eliminado':'Guardado en favoritos');render();return;
      }
      if(act==='cancel'){
        if(!confirm('¿Seguro que deseas cancelar esta cita?'))return;
        await call('/api/cliente/citas/'+id+'/cancelar','POST');await refreshAuth();notify('Cita cancelada');render();return;
      }
      if(act==='review'){showModal('Califica tu experiencia',`<form id="review-form" class="modal-form">
        <input name="cita" type="hidden" value="${id}"><label><span class="field-label">Calificación</span>
        <select class="field" name="calificacion" required><option value="5">★★★★★ · Excelente</option><option value="4">★★★★ · Muy buena</option><option value="3">★★★ · Regular</option><option value="2">★★ · Mala</option><option value="1">★ · Muy mala</option></select></label>
        <label><span class="field-label">Comentario</span><textarea class="field" name="comentario" maxlength="1000" rows="3"></textarea></label>
        <button class="btn btn-primary" type="submit">Publicar reseña</button></form>`);return;
      }
      if(act==='logout'){await call('/api/auth/logout','POST');state.csrf=null;state.user=null;state.citas=[];state.perfil=null;state.fav.clear();notify('Sesión cerrada');await navigate('inicio');return;}
    }catch(error){notify(error.message,true);el.disabled=false;}
  }
  document.addEventListener('click',action);
  document.addEventListener('submit',doForm);
  window.addEventListener('hashchange',()=>{if(!['servicios','empresas'].includes(location.hash.slice(1))){state.filtered=null;}if(!['servicios','empresas'].includes(location.hash.slice(1)))state.filters={texto:'',giro:'',estado:'',municipio:''};
    render();});
  document.addEventListener('change',async e=>{if(e.target.matches('#filters [name="estado"]')){
      const id=Number(e.target.value)||0;state.filters.estado=id?String(id):'';state.filters.municipio='';
      try{state.municipios=id?await api('/api/municipios/'+id):[];render();}catch(x){notify(x.message,true);}
    }});
  document.addEventListener('keydown',e=>{if(e.key==='Escape')closeModal();});
  init();
})();
