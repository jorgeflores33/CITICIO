'use strict';
// Paneles CITICIO: la navegacion del frontend NO sustituye la autorizacion del servidor.
const API = '/api/empresa';
const $ = id => document.getElementById(id);
const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const money = value => new Intl.NumberFormat('es-MX', {style:'currency',currency:'MXN'}).format(Number(value||0));
const fmtDate = value => {
  if (!value) return '—';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? escapeHtml(value) : new Intl.DateTimeFormat('es-MX',{dateStyle:'medium'}).format(d);
};
const fmtTime = value => {
  if (!value) return '—';
  const s=String(value);
  if (/^\d\d:\d\d/.test(s)) return s.slice(0,5);
  const d = new Date(s);
  return Number.isNaN(d.getTime()) ? escapeHtml(s) : new Intl.DateTimeFormat('es-MX',{hour:'2-digit',minute:'2-digit'}).format(d);
};
const roleLabels = {admin_empresa:'Administrador de empresa',encargado_sucursal:'Encargado de sucursal',empleado:'Empleado'};
const sections = {
  dashboard:{label:'Resumen',icon:'◫',title:'Resumen general',desc:'Controla los indicadores y la actividad de tu negocio.'},
  citas:{label:'Citas',icon:'▦',title:'Gestión de citas',desc:'Consulta y administra las reservaciones autorizadas.'},
  servicios:{label:'Servicios',icon:'✦',title:'Catálogo de servicios',desc:'Servicios asignados y precios vigentes de la empresa.'},
  sucursales:{label:'Sucursales',icon:'⌂',title:'Sucursales',desc:'Información de las sedes que puedes consultar.'},
  empleados:{label:'Empleados',icon:'♙',title:'Equipo de trabajo',desc:'Consulta tu personal y las asignaciones de sucursal.'},
  pagos:{label:'Pagos de servicios',icon:'▤',title:'Pagos de servicios',desc:'Movimientos de citas confirmados y pendientes.'},
  resenas:{label:'Reseñas',icon:'☆',title:'Valoraciones de clientes',desc:'Opiniones recibidas por tu empresa o sucursal.'},
  cobros:{label:'Cobros de CITICIO',icon:'◈',title:'Cobros a la empresa',desc:'Historial de cuotas y cargos de la plataforma.'},
  horarios:{label:'Horarios',icon:'◷',title:'Horarios de trabajo',desc:'Consulta y edita semanas completas de sucursales y empleados.'}
};
const menus = {
  admin_empresa:['dashboard','citas','servicios','sucursales','empleados','pagos','resenas','cobros','horarios'],
  encargado_sucursal:['dashboard','citas','servicios','empleados','pagos','resenas','horarios','sucursales'],
  empleado:['dashboard','citas','servicios','horarios']
};
const columns = {
  citas:[['id_cita','Folio'],['cliente','Cliente'],['sucursal','Sucursal'],['empleado','Profesional'],['hora_inicio','Fecha y hora','datetime'],['total_cita','Total','money'],['nombre_estado','Estado','status']],
  servicios:[['nombre','Servicio'],['descripcion','Descripción'],['precio','Precio vigente','money'],['id_servicio','Código']],
  sucursales:[['nombre','Sucursal'],['calle','Calle'],['numero_exterior','Número'],['colonia','Colonia'],['municipio','Municipio'],['estado','Estado'],['nombre_estado','Situación','status']],
  empleados:[['nombre','Nombre'],['apellido_paterno','Apellido paterno'],['apellido_materno','Apellido materno'],['telefono','Teléfono'],['correo','Correo'],['id_sucursal_principal','Sucursal ID'],['nombre_estado','Estado','status']],
  pagos:[['id_pago','Pago'],['id_cita','Cita'],['sucursal','Sucursal'],['monto','Importe','money'],['fecha_pago','Fecha','date'],['nombre_metodo','Método'],['nombre_estado','Estado','status']],
  resenas:[['id_resena','Reseña'],['id_cita','Cita'],['sucursal','Sucursal'],['calificacion_empresa','Empresa','rating'],['calificacion_sucursal','Sucursal','rating'],['comentario','Comentario'],['fecha_resena','Fecha','date']],
  cobros:[['id_cobro_empresa','Cargo'],['periodo_inicio','Desde','date'],['periodo_fin','Hasta','date'],['monto','Monto','money'],['fecha_vencimiento','Vencimiento','date'],['nombre_estado','Estado','status']],
  horarios:[['tipo','Tipo'],['empleado','Empleado / atención'],['sucursal','Sucursal'],['dia','Día'],['hora_entrada','Entrada','time'],['hora_salida','Salida','time']]
};
const demoRole = null; // Modo de demostración desactivado en la entrega
const demo = false;
let session = null, csrfToken = '', currentSection = 'dashboard', rows = [], searching = '';

// Los tokens antifalsificación están ligados al usuario actual; deben renovarse tras SignIn/SignOut.
async function securityToken(){
  if (demo) return;
  const r=await fetch(API+'/csrf',{credentials:'same-origin',cache:'no-store'});
  if (!r.ok) throw new Error('No se pudo renovar la seguridad de la sesión.');
  const body=await r.json();
  if (!body?.token) throw new Error('El servidor no devolvió un token de seguridad.');
  csrfToken=body.token;
}
async function request(method,route,payload,canRetry=true){
  if (demo) {
    if (method!=='GET') throw new Error('La vista previa es de consulta. Inicia sesión para guardar.');
    if (route==='/horarios/catalogos') return demoSchedules();
    if (route==='/cobros/metodos') return {transferenciaHabilitada:false,tarjetaHabilitada:false,solicitudes:[]};
    if (route==='/empleados/sin-acceso') return [];
    return demoData(route.split('/').pop());
  }
  if (method!=='GET' && !csrfToken) await securityToken();
  const response=await fetch(API+route,{
    method,credentials:'same-origin',cache:'no-store',
    headers:{...(payload?{'Content-Type':'application/json'}:{}),
             ...(method!=='GET'?{'X-CSRF-TOKEN':csrfToken}:{})},
    body:payload?JSON.stringify(payload):undefined
  });
  let result=null; try {result=await response.json();} catch { /* vacía */ }
  if (!response.ok){
    if(method!=='GET' && canRetry && response.status===400 &&
       (result?.code==='csrf_invalid' || /token de seguridad|antiforgery/i.test(result?.error||''))){
      await securityToken();
      return request(method,route,payload,false); // Como máximo un reintento. No se ejecutó la operación previa.
    }
    if(response.status===401){showLogin();throw new Error('Tu sesión expiró. Inicia sesión de nuevo.');}
    throw new Error(result?.error || `No se pudo completar la solicitud (${response.status}).`);
  }
  return result;
}
function showLogin(){ $('loginPage').hidden=false;$('appPage').hidden=true; }
function showApp(){ $('loginPage').hidden=true;$('appPage').hidden=false; }
function initSession(u){
  if (!demo && u.suscripcionActiva===false) {location.href=u.urlPago||"/EmpresaSuscripcion";return;}
  session=u;showApp();
  $('orgName').textContent = u.empresa||'Mi empresa';
  $('orgSubtitle').textContent=u.rol==='encargado_sucursal'?u.sucursal||'Sucursal asignada':'Gestión empresarial';
  $('orgAvatar').textContent=(u.empresa||'E').charAt(0).toUpperCase();
  $('userName').textContent=u.nombre;
  $('roleTitle').textContent=roleLabels[u.rol];
  $('userAvatar').textContent=u.nombre.charAt(0).toUpperCase();
  $('scopeLabel').textContent=u.rol==='encargado_sucursal'?(u.sucursal||'SUCURSAL').toUpperCase():u.rol==='empleado'?'MIS ASIGNACIONES':'MI EMPRESA';
  $('today').textContent=new Intl.DateTimeFormat('es-MX',{weekday:'short',day:'numeric',month:'short',year:'numeric'}).format(new Date());
  $('demoChip').hidden=!demo;
  $('sideNav').innerHTML=menus[u.rol].map(key=>`<button type="button" data-nav="${key}" class="nav-item"><span class="nav-icon">${sections[key].icon}</span><span>${sections[key].label}</span></button>`).join('');
  const requestedSection = new URLSearchParams(location.search).get('seccion');
  navigate(menus[u.rol].includes(requestedSection) ? requestedSection : 'dashboard');
}
async function start(){
  try {
    if (demo){initSession({rol:demoRole,nombre:'Usuario de prueba',empresa:'Studio Aura',sucursal:'Sucursal Centro'});return;}
    await securityToken();
    const response=await fetch(API+'/me',{credentials:'same-origin'});
    if(response.ok){const user=await response.json();if(menus[user.rol]){initSession(user);return;}}
    showLogin();
  }catch(e){showLogin();$('loginError').hidden=false;$('loginError').textContent=e.message;}
}
function showAlert(message,isError=false){const alert=$('alert');alert.hidden=false;alert.classList.toggle('error',isError);alert.textContent=message;}
function clearAlert(){$('alert').hidden=true;}
async function navigate(section){
  if (!session || !menus[session.rol]?.includes(section))return;
  currentSection=section;searching='';clearAlert();
  $('sidebar').classList.remove('open');
  document.querySelectorAll('[data-nav]').forEach(e=>e.classList.toggle('active',e.dataset.nav===section));
  $('pageTitle').textContent=sections[section].title;
  $('pageDesc').textContent=sections[section].desc;
  $('breadcrumbTitle').textContent=sections[section].label;
  $('pageActions').innerHTML=actionButtons(section);
  $('panel').innerHTML='<div class="loading">Cargando información de CITICIO…</div>';
  try{rows=await request('GET','/datos/'+section);renderPanel();}
  catch(e){$('panel').innerHTML=`<div class="section-card empty"><div class="symbol">⚠</div><strong>No se pudieron cargar los datos</strong>${escapeHtml(e.message)}</div>`;}
}
function actionButtons(section){
  if (demo) return '';
  if (section==='servicios' && session.rol==='admin_empresa') return '<button class="primary" data-create="servicio">+ Nuevo servicio</button>';
  if (section==='empleados' && session.rol==='admin_empresa') return '<button class="primary" data-create="personal">+ Registrar empleado / encargado</button> <button class="secondary" data-create="acceso">Crear acceso a empleado existente</button>';
  if (section==='sucursales' && session.rol==='admin_empresa') return '<button class="primary" data-create="sucursal">+ Registrar sucursal</button>';
  if (section==='horarios' && ['admin_empresa','encargado_sucursal'].includes(session.rol)) return '<button class="primary" data-create="semanaEmpleado">+ Semana de empleado</button> <button class="secondary" data-create="semanaSucursal">+ Semana de sucursal</button>';
  return '';
}
function statusBadge(value){
  const s=String(value||'Sin estado');
  const bad=/cancelada|inactiva|reembolsado/i.test(s);
  const good=/completada|confirmada|pagado|activo|activa/i.test(s);
  const cls=bad?'bad':good?'ok':/pendiente/i.test(s)?'wait':'other';
  return `<span class="status ${cls}">${escapeHtml(s.replaceAll('_',' '))}</span>`;
}
function renderValue(val,type){
  if (val===null || val===undefined || val==='') return '<span class="muted">—</span>';
  switch(type){
    case 'money':return `<strong class="table-main">${money(val)}</strong>`;
    case 'date':return fmtDate(val);
    case 'datetime':return `${fmtDate(val)} <span class="muted">${fmtTime(val)}</span>`;
    case 'time':return fmtTime(val);
    case 'status':return statusBadge(val);
    case 'rating':return val==null?'—':`<strong style="color:#d89e35">★ ${escapeHtml(val)}/5</strong>`;
    case 'role':return escapeHtml(roleLabels[val]||val);
    default:return escapeHtml(val);
  }
}
function renderPanel(){
  if(currentSection==='dashboard'){renderDashboard();return;}
  const config=columns[currentSection];
  if(!config){$('panel').innerHTML='Sección no configurada';return;}
  const available=rows.filter(row=>Object.values(row).some(v=>String(v??'').toLowerCase().includes(searching.toLowerCase())));
  const canStatus = currentSection==='citas' && session.rol!=='empleado' && !demo;
  const canPrice = currentSection==='servicios' && ['admin_empresa','empleado'].includes(session.rol) && !demo;
  const canPay = currentSection==='cobros' && session.rol==='admin_empresa' && !demo;
  const canEditBranch=currentSection==='sucursales' && session.rol==='admin_empresa' && !demo;
  const canServicesBranch=currentSection==='sucursales' && ['admin_empresa','encargado_sucursal'].includes(session.rol) && !demo;
  const canServicesEmployee=currentSection==='empleados' && ['admin_empresa','encargado_sucursal'].includes(session.rol) && !demo;
  const canEditWeek=currentSection==='horarios' && ['admin_empresa','encargado_sucursal'].includes(session.rol) && !demo;
  const actionColumn=canStatus||canPrice||canPay||canEditBranch||canServicesBranch||canServicesEmployee||canEditWeek;
  const rowActions=row=>{
    if(canPrice)return `<button class="table-action" data-price="${Number(row.id_servicio)}">Cambiar precio</button>`;
    if(canStatus)return `<button class="table-action" data-cita="${Number(row.id_cita)}">Cambiar estado</button>`;
    if(canPay)return row.nombre_estado==='cobro_pendiente'?`<button class="table-action" data-cobro="${Number(row.id_cobro_empresa)}">Ver pago</button>`:'—';
    if(canServicesBranch)return `${canEditBranch?`<button class="table-action" data-edit-branch="${Number(row.id_sucursal)}">Editar domicilio</button> <button class="table-action" data-bank-branch="${Number(row.id_sucursal)}">Cuenta bancaria</button>`:''} <button class="table-action" data-branch-services="${Number(row.id_sucursal)}">Servicios</button> <button class="table-action" data-week-branch="${Number(row.id_sucursal)}">Semana</button>`;
    if(canServicesEmployee)return `<button class="table-action" data-employee-services="${Number(row.id_empleado)}">Asignar servicios</button> <button class="table-action" data-week-employee="${Number(row.id_empleado)}" data-week-sucursal="${Number(row.id_sucursal_principal)}">Horario semanal</button>`;
    if(canEditWeek)return row.tipo==='sucursal'?`<button class="table-action" data-week-branch="${Number(row.id_sucursal)}">Editar semana</button>`:`<button class="table-action" data-week-employee="${Number(row.id_empleado)}" data-week-sucursal="${Number(row.id_sucursal)}">Editar semana</button>`;
    return '';
  };
  const html = available.length ? `<div class="table-wrap"><table><thead><tr>${config.map(c=>`<th>${c[1]}</th>`).join('')}${actionColumn?'<th>Acciones</th>':''}</tr></thead><tbody>${available.map(row=>`<tr>${config.map(c=>`<td>${renderValue(row[c[0]],c[2])}</td>`).join('')}${actionColumn?`<td class="table-actions">${rowActions(row)}</td>`:''}</tr>`).join('')}</tbody></table></div>` : `<div class="empty"><div class="symbol">▢</div><strong>Sin registros disponibles</strong>No hay información para mostrar con los filtros actuales.</div>`;
  $('panel').innerHTML=`<div class="panel-intro"><div><strong>${escapeHtml(sections[currentSection].label)}</strong><p>${available.length} registro${available.length===1?'':'s'} disponible${available.length===1?'':'s'}</p></div><input class="small-search" id="tableSearch" placeholder="Buscar en la tabla…" value="${escapeHtml(searching)}" aria-label="Filtrar registros" /></div><div class="table-card">${html}</div>${currentSection==='servicios'?'<div class="note">ⓘ Los precios son únicos para toda la empresa. Los encargados administran los servicios de su propia sucursal y los trabajadores solo los asignados.</div>':''}${currentSection==='sucursales' ? '<div class="note">ⓘ Los cobros de citas con tarjeta se centralizan en CITICIO Citas; no tienes que vincular Stripe Connect. Selecciona «Editar domicilio», «Servicios» o «Semana» en la sucursal. El horario se registra para toda la semana de una sola vez.</div>':''}${currentSection==='empleados' ? '<div class="note">ⓘ Cada persona necesita un usuario y correo de acceso únicos. En «Asignar servicios» marca qué puede realizar y en «Horario semanal» registra su jornada.</div>':''}${currentSection==='horarios'?'<div class="note">ⓘ Se muestran tanto los horarios de apertura de las sucursales como los turnos del personal. Los cambios de semana respetan las citas futuras.</div>':''}${currentSection==='cobros'?'<div id="cobrosInfo" class="note">Consultando opciones para pagar las cuotas de uso de CITICIO…</div>':''}`;
  if(currentSection==='cobros' && !demo && session.rol==='admin_empresa') renderCobrosInfo();
}
function renderDashboard(){
  const data=rows[0]||{};
  const type=session.rol;
  const cards=type==='admin_empresa'?[['Citas registradas',data.citas,'▦'],['Empleados',data.empleados,'♙'],['Sucursales',data.sucursales,'⌂'],['Ingresos confirmados',data.ingresos,'$',true]]:
    type==='encargado_sucursal'?[['Citas en sucursal',data.citas,'▦'],['Personal',data.empleados,'♙'],['Servicios',data.servicios,'✦'],['Ingresos confirmados',data.ingresos_confirmados,'$',true]]:
    [['Mis citas',data.citas,'▦'],['Citas pendientes',data.pendientes,'◷'],['Mis servicios',data.servicios,'✦'],['Horarios asignados',data.horarios,'◫']];
  $('panel').innerHTML=`<div class="kpi-grid">${cards.map(([title,num,icon,currency])=>`<div class="kpi-card"><div class="kpi-top"><span class="kpi-title">${title}</span><span class="kpi-symbol">${icon}</span></div><div class="kpi-value">${currency?money(num):escapeHtml(num??0)}</div><div class="kpi-foot">Datos de ${type==='empleado'?'tu cuenta':type==='encargado_sucursal'?'tu sucursal':'tu empresa'}</div></div>`).join('')}</div><div class="split-grid"><div class="section-card"><div class="section-head"><strong>Próximas citas</strong><small>Actividad de tu panel</small></div><div id="dashCitas"><div class="loading">Consultando citas…</div></div></div><div class="section-card"><div class="section-head"><strong>Acceso rápido</strong><small>Funciones principales</small></div><div class="quick-list">${(type==='empleado'?[['Mis citas','Consulta tus próximas citas','citas','▦'],['Mis servicios','Actualiza precios autorizados','servicios','✦'],['Mi horario','Revisa tu jornada semanal','horarios','◷']]:[['Citas','Supervisa las reservaciones','citas','▦'],['Servicios','Consulta el catálogo y precios','servicios','✦'],['Personal','Consulta los integrantes de tu equipo','empleados','♙']]).map(([a,b,c,icon])=>`<button class="quick-row" data-nav="${c}" style="width:100%;text-align:left;background:none;border-left:0;border-right:0;border-top:0;cursor:pointer"><span class="quick-icon">${icon}</span><span><strong>${a}</strong><p>${b}</p></span></button>`).join('')}</div></div></div>`;
  request('GET','/datos/citas').then(data=>{
    if(currentSection!=='dashboard')return;
    const future=data.filter(x=>{const d=new Date(x.hora_inicio);return !Number.isNaN(d.getTime()) && d.getTime()>=Date.now();}).sort((a,b)=>new Date(a.hora_inicio)-new Date(b.hora_inicio)).slice(0,5);
    $('dashCitas').innerHTML=future.length?`<div class="table-wrap"><table><thead><tr><th>Cliente</th><th>Fecha</th><th>Estado</th></tr></thead><tbody>${future.map(x=>`<tr><td class="table-main">${escapeHtml(x.cliente)}</td><td>${fmtDate(x.hora_inicio)}</td><td>${statusBadge(x.nombre_estado)}</td></tr>`).join('')}</tbody></table></div>`:'<div class="empty"><div class="symbol">☷</div><strong>No hay próximas citas</strong>Las nuevas reservaciones aparecerán aquí.</div>';
  }).catch(()=>{$('dashCitas').innerHTML='<div class="empty">No se pudo consultar la agenda.</div>';});
}
function demoData(section){
  const base=[{id_cita:1001,cliente:'María López',sucursal:'Sucursal Centro',empleado:'Ana Pérez',hora_inicio:new Date(Date.now()+86400000).toISOString(),total_cita:450,nombre_estado:'cita_confirmada'},
    {id_cita:1002,cliente:'Carlos Díaz',sucursal:'Sucursal Centro',empleado:'Ana Pérez',hora_inicio:new Date(Date.now()+2*86400000).toISOString(),total_cita:250,nombre_estado:'cita_pendiente'},
    {id_cita:1003,cliente:'Andrea Ruiz',sucursal:'Sucursal Norte',empleado:'Luis Mora',hora_inicio:new Date(Date.now()+3*86400000).toISOString(),total_cita:680,nombre_estado:'cita_confirmada'}];
  switch(section){
    case 'dashboard':return [{citas:48,empleados:12,sucursales:3,clientes:124,ingresos:28750,ingresos_confirmados:9400,servicios:9,resenas:18,pendientes:6,horarios:5}];
    case 'citas':return demoRole==='empleado'?base.filter(x=>x.empleado==='Ana Pérez'):demoRole==='encargado_sucursal'?base.filter(x=>x.sucursal==='Sucursal Centro'):base;
    case 'servicios':return [{id_servicio:10,nombre:'Corte clásico',descripcion:'Servicio profesional con asesoría',precio:250},{id_servicio:11,nombre:'Tratamiento capilar',descripcion:'Cuidado y restauración',precio:450},{id_servicio:12,nombre:'Barba y perfilado',descripcion:'Diseño y acabado',precio:180}];
    case 'empleados':return [{id_empleado:8,nombre:'Ana',apellido_paterno:'Pérez',telefono:'9610000001',correo:'ana@ejemplo.com',id_sucursal_principal:2,nombre_estado:'empleado_activo'},{id_empleado:9,nombre:'Luis',apellido_paterno:'Mora',telefono:'9610000002',correo:'luis@ejemplo.com',id_sucursal_principal:2,nombre_estado:'empleado_activo'}];
    case 'sucursales':return [{nombre:'Sucursal Centro',calle:'Av. Central',numero_exterior:'15',colonia:'Centro',municipio:'Tuxtla Gutiérrez',estado:'Chiapas',nombre_estado:'sucursal_activa'},{nombre:'Sucursal Norte',calle:'Blvd. Norte',numero_exterior:'90',colonia:'Residencial',municipio:'Tuxtla Gutiérrez',estado:'Chiapas',nombre_estado:'sucursal_activa'}];
    case 'pagos':return [{id_pago:90,id_cita:1001,sucursal:'Sucursal Centro',monto:450,fecha_pago:new Date().toISOString(),nombre_metodo:'Tarjeta',nombre_estado:'pago_pagado'}];
    case 'resenas':return [{id_resena:33,id_cita:1001,sucursal:'Sucursal Centro',calificacion_empresa:5,calificacion_sucursal:5,comentario:'Excelente atención',fecha_resena:new Date().toISOString()}];
    case 'cobros':return [{id_cobro_empresa:14,periodo_inicio:'2026-11-01',periodo_fin:'2026-11-30',monto:399,fecha_vencimiento:'2026-11-15',nombre_estado:'cobro_pendiente'}];
    case 'horarios':return [{empleado:'Ana Pérez',sucursal:'Sucursal Centro',dia:'Lunes',hora_entrada:'09:00',hora_salida:'17:00'},{empleado:'Ana Pérez',sucursal:'Sucursal Centro',dia:'Martes',hora_entrada:'09:00',hora_salida:'17:00'}];
    default:return [];
  }
}

let submitDialog = null;
function openDialog(title, content, submit){
  submitDialog=submit; $('dialogTitle').textContent=title;$('dialogContent').innerHTML=content;
  $('dialogError').hidden=true;$('actionDialog').showModal();
}
function field(label,name,type='text',placeholder='',required=true){
  return `<div class="field"><label for="field_${name}">${label}</label><input id="field_${name}" name="${name}" type="${type}" placeholder="${placeholder}" ${required?'required':''}></div>`;
}
function openPrice(id){
  const service=rows.find(x=>Number(x.id_servicio)===id);
  if(!service)return;
  openDialog('Actualizar precio de servicio',`<p class="hint">${escapeHtml(service.nombre)} · Precio actual: ${money(service.precio)}</p>${field('Nuevo precio (MXN)','precio','number','0.00')}<div class="note">Este cambio se aplicará al servicio en todas las sucursales de la empresa.</div>`,
    async data=>request('POST','/precios',{idServicio:id,precio:Number(data.get('precio'))}));
  $('field_precio').step='0.01';$('field_precio').min='0';$('field_precio').value=service.precio;
}
function openCita(id){
  const cita=rows.find(x=>Number(x.id_cita)===id);if(!cita)return;
  const allowed=cita.nombre_estado==='cita_pendiente'?['cita_confirmada','cita_cancelada']:cita.nombre_estado==='cita_confirmada'?['cita_completada','cita_cancelada']:[];
  if(!allowed.length){showAlert('No es posible cambiar el estado de una cita finalizada.',true);return;}
  openDialog('Actualizar estado de cita',`<p class="hint">Cita #${id} · ${escapeHtml(cita.cliente)}</p><div class="field"><label for="field_estado">Nuevo estado</label><select id="field_estado" name="estado" required>${allowed.map(s=>`<option value="${s}">${s.replaceAll('_',' ')}</option>`).join('')}</select></div>`,
    async data=>request('POST','/citas/estado',{idCita:id,estado:data.get('estado')}));
}
function openServicioNuevo(){
  openDialog('Nuevo servicio',`${field('Nombre del servicio','nombre','text','Ej. Corte premium')}${field('Descripción','descripcion','text','Breve descripción',false)}${field('Precio en MXN','precio','number','350.00')}<div class="note">El servicio se crea en tu empresa. Asignarlo a sucursales se realiza mediante el catálogo existente de CITICIO.</div>`,
    async data=>request('POST','/servicios',{nombre:data.get('nombre'),descripcion:data.get('descripcion'),precio:Number(data.get('precio'))}));
  $('field_precio').min='0';$('field_precio').step='0.01';
}
async function openAccesoNuevo(){
  try {
    const available=await request('GET','/empleados/sin-acceso');
    if(!available.length){showAlert('No hay empleados sin cuenta. Registra personal desde este mismo módulo.',true);return;}
    openDialog('Crear acceso de empleado existente',`<div class="field"><label>Empleado</label><select name="idEmpleado" required>${available.map(e=>`<option value="${Number(e.id_empleado)}">${escapeHtml(e.empleado)} · ${escapeHtml(e.sucursal||'Sin sucursal')}</option>`).join('')}</select></div><div class="field"><label>Rol a asignar</label><select name="rol"><option value="empleado">Empleado</option><option value="encargado_sucursal">Encargado de sucursal</option></select></div>${field('Nombre de usuario','nombreUsuario','text','ana.perez')}${field('Correo de acceso','correo','email','usuario@empresa.com')}${field('Contraseña temporal','contrasena','password','8+; mayúscula, minúscula, número y símbolo')}`,async d=>request('POST','/accesos',{idEmpleado:Number(d.get('idEmpleado')),rol:d.get('rol'),nombreUsuario:d.get('nombreUsuario'),correo:d.get('correo'),contrasena:d.get('contrasena')}));
    $('field_contrasena').minLength=8;
  }catch(e){showAlert(e.message,true);}
}
async function openPersonalNuevo(){
  try {
    const branches=await request('GET','/datos/sucursales');
    const active=branches.filter(b=>b.nombre_estado==='sucursal_activa');
    if(!active.length){showAlert('Primero crea una sucursal de tu empresa.',true);return;}
    const options=active.map(b=>`<option value="${Number(b.id_sucursal)}">${escapeHtml(b.nombre)}</option>`).join('');
    openDialog('Registrar empleado o encargado',`
      <div class="field"><label for="newBranch">Sucursal asignada</label><select name="idSucursal" id="newBranch" required><option value="">Seleccionar sucursal</option>${options}</select></div>
      <div class="field"><label for="newRole">Rol de acceso</label><select name="rol" id="newRole" required><option value="empleado">Empleado</option><option value="encargado_sucursal">Encargado de sucursal</option></select></div>
      ${field('Nombre(s)','nombre','text','Nombre')}
      ${field('Apellido paterno','apellidoPaterno','text','Apellido paterno')}
      ${field('Apellido materno','apellidoMaterno','text','Opcional',false)}
      ${field('Teléfono (10 a 15 números)','telefono','tel','10 a 15 números')}
      ${field('Correo electrónico único','correo','email','persona@empresa.com')}
      ${field('Nombre de usuario único','nombreUsuario','text','nombre.apellido')}
      ${field('Contraseña segura (8+ caracteres)','contrasena','password','Contraseña temporal')}
      <div class="field"><label>Servicios que realizará el trabajador</label><div id="newPersonalServices" class="check-group">Selecciona primero una sucursal.</div></div>
      <div class="note">La cuenta será individual, con rol <strong>empleado</strong> o <strong>encargado_sucursal</strong>. No es posible crear otro administrador. Para probar dos personas a la vez usa navegadores o perfiles distintos.</div>
    `, async d=>{
        if(!/^\d{10,15}$/.test(String(d.get('telefono'))))throw Error('El teléfono debe tener de 10 a 15 dígitos.');
        if(!/^(?=.{8,128}$)(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^A-Za-z0-9\s]).*$/.test(String(d.get('contrasena'))))throw Error('Usa 8+ caracteres, mayúscula, minúscula, número y símbolo.');
        const ids=[...$('newPersonalServices').querySelectorAll('input[name="servicioId"]:checked')].map(x=>Number(x.value));
        if(d.get('rol')==='empleado'&&!ids.length)throw Error('Selecciona al menos un servicio que realizará el empleado.');
        return request('POST','/personal',{
          idSucursal:Number(d.get('idSucursal')),rol:d.get('rol'),
          nombre:d.get('nombre'),apellidoPaterno:d.get('apellidoPaterno'),
          apellidoMaterno:d.get('apellidoMaterno'),telefono:d.get('telefono'),
          correo:d.get('correo'),nombreUsuario:d.get('nombreUsuario'),
          contrasena:d.get('contrasena'),idServicios:ids
        });
      });
    $('field_telefono').pattern='[0-9]{10,15}';$('field_telefono').maxLength=15;
    $('field_correo').maxLength=254;$('field_contrasena').minLength=8;
    const branch=$('newBranch'),view=$('newPersonalServices');
    branch.addEventListener('change',async()=>{
      if(!branch.value){view.textContent='Selecciona primero una sucursal.';return;}
      view.textContent='Cargando servicios...';
      try{
        const list=await request('GET',`/sucursales/${Number(branch.value)}/servicios`);
        const available=list.filter(x=>x.asignado);
        view.innerHTML=available.length?available.map(x=>`<label class="check-line"><input type="checkbox" name="servicioId" value="${Number(x.id_servicio)}"> ${escapeHtml(x.nombre)} · ${money(x.precio)}</label>`).join(''):'<div class="note">Esta sucursal no tiene servicios. Ve a Sucursales → Servicios y habilítalos antes de crear al empleado.</div>';
      }catch(e){view.textContent=e.message;}
    });
  }catch(e){showAlert(e.message,true);}
}
// Sucursales: se conserva el diseño de los formularios existentes y se usan los IDs oficiales.
async function publicList(url){
  const r=await fetch(url,{credentials:'same-origin',cache:'no-store'});
  const data=await r.json();
  if(!r.ok)throw Error(data?.error||'No se pudo cargar el catálogo.');
  if(!Array.isArray(data))throw Error('El catálogo no devolvió una lista.');
  return data;
}
function fillSelect(el,list,placeholder){
  el.replaceChildren(new Option(placeholder,''));
  list.forEach(item=>el.add(new Option(item.nombre,String(item.id))));
  el.disabled=!list.length;
}
async function openSucursalNueva(existingId=null){
  let original=null;
  try {if(existingId)original=await request('GET',`/sucursales/${Number(existingId)}/editar`);}catch(e){showAlert(e.message,true);return;}
  openDialog(original?'Editar sucursal':'Registrar nueva sucursal',`
    ${field('Nombre de la sucursal','nombre','text','Sucursal Norte')}
    <div class="field"><label>Estado</label><select id="branchEstado" name="idEstado" required><option value="">Cargando estados...</option></select></div>
    <div class="field"><label>Municipio</label><select id="branchMunicipio" name="idMunicipio" required disabled><option value="">Selecciona estado</option></select></div>
    ${field('Código postal (para filtrar)','codigoPostal','text','29000',false)}
    <div class="field"><label>Colonia / asentamiento</label><select id="branchColonia" name="idColonia" required disabled><option value="">Selecciona municipio</option></select></div>
    ${field('Calle','calle','text','Avenida Central')}
    ${field('Número exterior','numeroExterior','text','123')}
    ${field('Número interior','numeroInterior','text','Opcional',false)}
    ${!original?`${field('Banco de la sucursal','bancoSucursal','text','Nombre del banco')}${field('Titular bancario','titularSucursal','text','Titular de la cuenta')}${field('CLABE (18 dígitos)','clabeSucursal','text','18 números')}`:''}
    <div class="note" id="branchGeoHelp">Selecciona estado, municipio y colonia del catálogo nacional. El horario semanal se administra desde la tabla de sucursales.</div>
  `,async d=>request(original?'PUT':'POST',original?`/sucursales/${Number(existingId)}`:'/sucursales',{
    nombre:d.get('nombre'),idEstado:Number(d.get('idEstado')),idMunicipio:Number(d.get('idMunicipio')),
    idColonia:Number(d.get('idColonia')),codigoPostal:d.get('codigoPostal')||null,
    calle:d.get('calle'),numeroExterior:d.get('numeroExterior'),numeroInterior:d.get('numeroInterior')||null,
    ...(!original?{bancoSucursal:d.get('bancoSucursal'),titularSucursal:d.get('titularSucursal'),clabeSucursal:d.get('clabeSucursal')}:{})
  }));
  const estado=$('branchEstado'),mun=$('branchMunicipio'),col=$('branchColonia'),cp=$('field_codigoPostal');
  const help=$('branchGeoHelp');cp.maxLength=5;cp.pattern='[0-9]{5}';
  let version=0;
  const loadMunicipios=async()=>{
    const v=++version;fillSelect(mun,[],'Selecciona estado');fillSelect(col,[],'Selecciona municipio');
    if(!estado.value)return;
    try{const data=await publicList('/api/publico/geo/municipios?idEstado='+encodeURIComponent(estado.value));
      if(v===version)fillSelect(mun,data,data.length?'Selecciona municipio':'No hay municipios importados');}
    catch(e){help.textContent=e.message;}
  };
  const loadCols=async()=>{
    const v=++version;
    if(!mun.value){fillSelect(col,[],'Selecciona municipio');return;}
    if(cp.value && !/^\d{5}$/.test(cp.value)){help.textContent='Código postal: usa 5 dígitos o déjalo vacío.';return;}
    fillSelect(col,[],'Cargando colonias…');
    const q=new URLSearchParams({idMunicipio:mun.value});if(cp.value)q.set('cp',cp.value);
    try{const data=await publicList('/api/publico/geo/colonias?'+q);
      if(v===version){fillSelect(col,data,data.length?'Selecciona colonia':'No hay colonias');help.textContent=data.length?'Selecciona la colonia y completa la calle.':'No hay colonias con ese filtro. Revisa SEPOMEX o el CP.';}}
    catch(e){if(v===version)help.textContent=e.message;}
  };
  try{
    fillSelect(estado,await publicList('/api/publico/geo/estados'),'Selecciona estado');
    if(original){
      $('field_nombre').value=original.nombre||'';$('field_calle').value=original.calle||'';
      $('field_numeroExterior').value=original.numero_exterior||'';
      $('field_numeroInterior').value=original.numero_interior||'';
      estado.value=String(original.id_entidad_federativa);await loadMunicipios();
      mun.value=String(original.id_municipio);cp.value=original.codigo_postal||'';
      await loadCols();col.value=String(original.id_colonia);
    }
  }catch(e){help.textContent=e.message;}
  estado.addEventListener('change',async()=>{cp.value='';await loadMunicipios();});
  mun.addEventListener('change',async()=>{cp.value='';await loadCols();});
  cp.addEventListener('change',loadCols);
}
async function openServiciosSucursal(id){
  try{
    const list=await request('GET',`/sucursales/${id}/servicios`);
    if(!list.length){showAlert('Crea primero servicios en el catálogo de empresa.',true);return;}
    openDialog('Servicios de la sucursal',`
      <div class="note">Habilita servicios para poder asignarlos al personal y reservar citas. Los precios pertenecen a la empresa. Duración: minutos por cita.</div>
      <div class="check-group">${list.map(x=>`<div class="service-line">
       <label class="check-line"><input name="servicio" type="checkbox" value="${Number(x.id_servicio)}" ${x.asignado?'checked':''}>
       <span><strong>${escapeHtml(x.nombre)}</strong><small>${money(x.precio)}${x.asignado?' · Habilitado':''}</small></span></label>
       <input type="number" class="duration-field" data-duration="${Number(x.id_servicio)}" min="1" max="720" step="1" value="${Number(x.duracion_minutos||30)}" aria-label="Duración en minutos de ${escapeHtml(x.nombre)}">
      </div>`).join('')}</div><div class="note">Esta operación habilita o actualiza servicios. Desmarcar uno que ya estaba habilitado no lo elimina de citas existentes.</div>
    `,async()=>{
      const selected=[...$('dialogContent').querySelectorAll('input[name="servicio"]:checked')].map(x=>({
        idServicio:Number(x.value),duracionMinutos:Number($('dialogContent').querySelector(`[data-duration="${x.value}"]`).value)
      }));
      if(selected.some(x=>x.duracionMinutos<1 || x.duracionMinutos>720))throw Error('Duración de servicio: entre 1 y 720 minutos.');
      return request('POST',`/sucursales/${id}/servicios`,{servicios:selected});
    });
  }catch(e){showAlert(e.message,true);}
}
async function openServiciosEmpleado(id){
  try{
    const data=await request('GET',`/empleados/${id}/servicios`);
    const list=data.servicios||[];
    openDialog('Asignar servicios a empleado',`
      <div class="note">El trabajador solo podrá ofrecer servicios habilitados en su sucursal. Sus citas y precios se limitan a las asignaciones autorizadas.</div>
      <div class="check-group">${list.length?list.map(x=>`<label class="check-line ${!x.disponible_sucursal?'check-disabled':''}">
       <input type="checkbox" name="servicioId" value="${Number(x.id_servicio)}" ${x.asignado?'checked':''} ${!x.disponible_sucursal?'disabled':''}>
       <span><strong>${escapeHtml(x.nombre)}</strong><small>${money(x.precio)}${!x.disponible_sucursal?' · Primero habilitar en sucursal':''}</small></span>
      </label>`).join(''):'<p>Esta empresa aún no tiene servicios. Registra los servicios y habilítalos en la sucursal.</p>'}</div>
    `,async()=>{
      const ids=[...$('dialogContent').querySelectorAll('input[name="servicioId"]:checked')].map(x=>Number(x.value));
      return request('POST',`/empleados/${id}/servicios`,{idServicios:ids});
    });
  }catch(e){showAlert(e.message,true);}
}

const weekDays=[['1','Lunes'],['2','Martes'],['3','Miércoles'],['4','Jueves'],['5','Viernes'],['6','Sábado'],['7','Domingo']];
async function openHorarioSemana(tipo,fixedSucursal=null,fixedEmpleado=null){
  try{
    const catalog=await request('GET','/horarios/catalogos');
    if(!catalog.sucursales?.length){showAlert('No hay sucursales activas disponibles.',true);return;}
    const branchOptions=optionsFor(catalog.sucursales,'id_sucursal','nombre','Selecciona sucursal');
    openDialog(tipo==='sucursal'?'Horario semanal de sucursal':'Horario semanal de empleado',`
      <div class="field"><label>Sucursal</label><select id="weekBranch" required name="idSucursal">${branchOptions}</select></div>
      ${tipo==='empleado'?'<div class="field"><label>Empleado</label><select id="weekEmployee" name="idEmpleado" required><option value="">Selecciona sucursal</option></select></div>':''}
      <div class="note" id="weekHint">Marca los días laborables. Los no seleccionados quedarán sin horario. Puedes asignar una hora diferente a cada día y guardar toda la semana con un clic.</div>
      <div class="week-grid">${weekDays.map(([id,name])=>`<div class="week-day" data-week-row="${id}">
        <label class="check-line"><input type="checkbox" name="activo_${id}" data-day-check="${id}"> <strong>${name}</strong></label>
        <input type="time" name="inicio_${id}" value="09:00" aria-label="${name} apertura o entrada">
        <span>–</span><input type="time" name="fin_${id}" value="${tipo==='sucursal'?'18:00':'17:00'}" aria-label="${name} cierre o salida">
      </div>`).join('')}</div>
      <div class="note">El horario de empleado debe estar dentro de la apertura de la sucursal. Los cambios que invaliden citas futuras serán rechazados.</div>
    `,async d=>{
      const idSucursal=Number(d.get('idSucursal')),idEmpleado=tipo==='empleado'?Number(d.get('idEmpleado')):null;
      if(!idSucursal || (tipo==='empleado'&&!idEmpleado))throw Error('Selecciona sucursal y empleado antes de guardar.');
      const dias=weekDays.filter(([id])=>d.has(`activo_${id}`)).map(([id])=>({idDia:Number(id),inicio:d.get(`inicio_${id}`),fin:d.get(`fin_${id}`)}));
      if(dias.some(x=>!/^\d{2}:\d{2}$/.test(x.inicio)||!/^\d{2}:\d{2}$/.test(x.fin)||x.fin<=x.inicio))
        throw Error('Revisa las horas: la salida debe ser posterior a la entrada.');
      if(!dias.length && !confirm('Vas a dejar esta semana sin horarios. ¿Continuar?'))throw Error('Operación cancelada.');
      return request('PUT','/horarios/semana',{tipo,idSucursal,idEmpleado,dias});
    });
    const branch=$('weekBranch'),employee=$('weekEmployee'),hint=$('weekHint');
    const updateEmployees=()=>{
      if(!employee)return;
      const previous=employee.value;
      const people=(catalog.empleados||[]).filter(e=>String(e.id_sucursal)===branch.value);
      employee.innerHTML=optionsFor(people,'id_empleado','nombre',people.length?'Selecciona empleado':'Sin empleados en esta sucursal');
      if(people.some(e=>String(e.id_empleado)===previous))employee.value=previous;
    };
    const refreshWeek=async()=>{
      const idSucursal=Number(branch.value),idEmpleado=employee?Number(employee.value):null;
      if(!idSucursal || (employee&&!idEmpleado))return;
      hint.textContent='Cargando horario semanal...';
      try{
        const qs=new URLSearchParams({tipo,idSucursal:String(idSucursal)});
        if(idEmpleado)qs.set('idEmpleado',String(idEmpleado));
        const actual=await request('GET','/horarios/semana?'+qs);
        weekDays.forEach(([day])=>{
          const found=actual.find(x=>Number(x.id_dia)===Number(day));
          const check=$(`dialogContent`).querySelector(`[data-day-check="${day}"]`);
          const row=$(`dialogContent`).querySelector(`[data-week-row="${day}"]`);
          check.checked=!!found;
          row.querySelector(`[name="inicio_${day}"]`).value=found?.inicio||'09:00';
          row.querySelector(`[name="fin_${day}"]`).value=found?.fin||(tipo==='sucursal'?'18:00':'17:00');
        });
        hint.textContent=actual.length?`Se cargaron ${actual.length} días registrados. Puedes editarlos o cerrar días desmarcándolos.`:'No hay horarios anteriores. Marca los días que deseas registrar.';
      }catch(e){hint.textContent=e.message;}
    };
    branch.addEventListener('change',()=>{updateEmployees();refreshWeek();});
    if(employee)employee.addEventListener('change',refreshWeek);
    if(fixedSucursal)branch.value=String(fixedSucursal);
    else if(catalog.sucursales.length===1)branch.value=String(catalog.sucursales[0].id_sucursal);
    updateEmployees();
    if(employee&&fixedEmpleado)employee.value=String(fixedEmpleado);
    await refreshWeek();
  }catch(e){showAlert(e.message,true);}
}
function demoSchedules(){
  return {sucursales:[{id_sucursal:1,nombre:'Sucursal Centro'}],
    empleados:[{id_empleado:8,nombre:'Ana Pérez',id_sucursal:1}],
    dias:[{id_dia:1,nombre:'Lunes'},{id_dia:2,nombre:'Martes'},{id_dia:3,nombre:'Miércoles'}],
    horariosSucursal:[{id_sucursal:1,id_dia:1,apertura:'09:00',cierre:'18:00'}]};
}
function optionsFor(records,key,label,placeholder){
  return `<option value="">${placeholder}</option>`+records.map(r=>`<option value="${Number(r[key])}">${escapeHtml(r[label])}</option>`).join('');
}
async function openHorarioSucursal(){
  try{
    const c=await request('GET','/horarios/catalogos');
    if(!c.sucursales.length||!c.dias.length){showAlert('Necesitas sucursales activas y el catálogo de días de la semana.',true);return;}
    openDialog('Registrar horario de sucursal',`
      <div class="field"><label>Sucursal</label><select name="idSucursal" required>${optionsFor(c.sucursales,'id_sucursal','nombre','Selecciona sucursal')}</select></div>
      <div class="field"><label>Día</label><select name="idDia" required>${optionsFor(c.dias,'id_dia','nombre','Selecciona día')}</select></div>
      ${field('Hora de apertura','apertura','time','09:00')}${field('Hora de cierre','cierre','time','18:00')}
      <div class="note">Registra primero el horario de atención de la sucursal. Después podrás programar a los empleados dentro de esa franja.</div>
    `,async d=>request('POST','/horarios/sucursal',{
      idSucursal:Number(d.get('idSucursal')),idDia:Number(d.get('idDia')),
      apertura:d.get('apertura'),cierre:d.get('cierre')
    }));
  }catch(e){showAlert(e.message,true);}
}
async function openHorarioEmpleado(){
  try{
    const c=await request('GET','/horarios/catalogos');
    if(!c.empleados.length||!c.sucursales.length||!c.dias.length){showAlert('Registra primero una sucursal activa, los días y el empleado.',true);return;}
    openDialog('Registrar horario de empleado',`
      <div class="field"><label>Sucursal asignada</label><select id="scheduleBranch" name="idSucursal" required>${optionsFor(c.sucursales,'id_sucursal','nombre','Selecciona sucursal')}</select></div>
      <div class="field"><label>Empleado</label><select id="scheduleEmployee" name="idEmpleado" required><option value="">Selecciona sucursal</option></select></div>
      <div class="field"><label>Día</label><select id="scheduleDay" name="idDia" required>${optionsFor(c.dias,'id_dia','nombre','Selecciona día')}</select></div>
      ${field('Hora de entrada','entrada','time','09:00')}${field('Hora de salida','salida','time','17:00')}
      <div class="note" id="scheduleHelp">Primero registra los horarios de la sucursal. Los turnos del empleado deben estar dentro del horario de atención.</div>
    `,async d=>request('POST','/horarios/empleado',{
      idEmpleado:Number(d.get('idEmpleado')),idSucursal:Number(d.get('idSucursal')),
      idDia:Number(d.get('idDia')),entrada:d.get('entrada'),salida:d.get('salida')
    }));
    const branch=$('scheduleBranch'),emp=$('scheduleEmployee'),dia=$('scheduleDay'),help=$('scheduleHelp');
    const refresh=()=>{
      const previous=emp.value;
      const employees=c.empleados.filter(e=>String(e.id_sucursal)===branch.value);
      emp.innerHTML=optionsFor(employees,'id_empleado','nombre',employees.length?'Selecciona empleado':'Sin empleados asignados');
      if(employees.some(e=>String(e.id_empleado)===previous))emp.value=previous;
      const hours=c.horariosSucursal.filter(h=>String(h.id_sucursal)===branch.value && String(h.id_dia)===dia.value);
      help.textContent=hours.length?'Horario de sucursal: '+hours.map(h=>h.apertura+'–'+h.cierre).join(', '):'No hay horario de sucursal para ese día. Regístralo primero en «+ Horario sucursal».';
    };
    branch.addEventListener('change',refresh);dia.addEventListener('change',refresh);refresh();
  }catch(e){showAlert(e.message,true);}
}
let paymentOptions=null;
async function renderCobrosInfo(){
  try{
    paymentOptions=await request('GET','/cobros/metodos');
    if(currentSection!=='cobros')return;
    const info=$('cobrosInfo');if(!info)return;
    const req=paymentOptions.solicitudes||[];
    const pending=req.filter(x=>x.estado==='en_revision').length;
    info.innerHTML=`<strong>Pago por uso de la plataforma CITICIO</strong>
      <p>Transferencia SPEI: ${paymentOptions.transferenciaHabilitada?`Banco ${escapeHtml(paymentOptions.banco)} · Beneficiario ${escapeHtml(paymentOptions.titular)} · CLABE <strong>${escapeHtml(paymentOptions.clabe)}</strong>`:'Aún no configurada por CITICIO; no realices depósitos.'}</p>
      <p>Tarjeta: no disponible mientras no se integre y verifique la pasarela de pago.</p>
      <p>${pending?`${pending} transferencia(s) reportada(s) pendientes de revisión.`:'El pago sólo aparece como confirmado después de que CITICIO verifique la transferencia.'}</p>`;
  }catch(e){const info=$('cobrosInfo');if(info)info.textContent=e.message;}
}
async function openCobro(id){
  const charge=rows.find(r=>Number(r.id_cobro_empresa)===id);
  if(!charge)return;
  try{
    const opts=paymentOptions||await request('GET','/cobros/metodos');
    const estado=(opts.solicitudes||[]).find(s=>Number(s.id_cobro_empresa)===id)?.estado;
    if(estado==='en_revision'){showAlert('Esta cuota ya tiene una transferencia reportada; espera la revisión de CITICIO.',true);return;}
    if(!opts.transferenciaHabilitada){showAlert('CITICIO aún no configuró su cuenta bancaria para transferencias. No realices depósitos.',true);return;}
    openDialog('Pagar cuota de CITICIO',`
      <p class="hint">Cargo #${id} · Importe ${money(charge.monto)}</p>
      <div class="field"><label>Forma de pago</label><select name="metodo"><option value="transferencia_spei">Transferencia bancaria SPEI</option></select></div>
      <div class="note"><strong>Datos de transferencia</strong><p>Banco: ${escapeHtml(opts.banco)}</p><p>Titular: ${escapeHtml(opts.titular)}</p><p>CLABE: <strong>${escapeHtml(opts.clabe)}</strong></p><p>Concepto sugerido: CITICIO cargo #${id}</p></div>
      ${field('Referencia de transferencia realizada','referencia','text','Clave de rastreo / referencia')}
      <div class="note">Registrar una referencia NO confirma el pago. CITICIO revisará el ingreso antes de activar el estado «cobro_pagado».</div>
    `,async d=>request('POST','/cobros/transferencia',{idCobroEmpresa:id,referencia:d.get('referencia')}));
    $('field_referencia').minLength=6;$('field_referencia').maxLength=120;
  }catch(e){showAlert(e.message,true);}
}
$('loginForm').addEventListener('submit',async ev=>{
  ev.preventDefault();$('loginError').hidden=true;$('loginButton').disabled=true;
  try{await request('POST','/login',{correo:$('loginEmail').value,contrasena:$('loginPass').value});await securityToken();const u=await request('GET','/me');initSession(u);$('loginPass').value='';}
  catch(e){$('loginError').textContent=e.message;$('loginError').hidden=false;}
  finally{$('loginButton').disabled=false;}
});
$('logoutBtn').addEventListener('click',async()=>{if(demo){location.href='/empresa/index.html';return;}try{await request('POST','/logout');csrfToken='';location.href='/empresa/index.html';}catch(e){showAlert(e.message,true);}});
$('mobileMenu').addEventListener('click',()=>$('sidebar').classList.toggle('open'));
$('cancelDialog').addEventListener('click',()=>$('actionDialog').close());
$('dialogForm').addEventListener('submit',async ev=>{
  ev.preventDefault();if(!submitDialog)return;
  const button=$('saveDialog');button.disabled=true;$('dialogError').hidden=true;
  try{const result=await submitDialog(new FormData(ev.currentTarget));$('actionDialog').close();await navigate(currentSection);showAlert(result?.mensaje||'Operación realizada correctamente.');}
  catch(e){$('dialogError').textContent=e.message;$('dialogError').hidden=false;}
  finally{button.disabled=false;}
});
document.body.addEventListener('click',ev=>{
  const nav=ev.target.closest('[data-nav]');if(nav){navigate(nav.dataset.nav);return;}
  const editBranch=ev.target.closest('[data-edit-branch]');if(editBranch){openSucursalNueva(Number(editBranch.dataset.editBranch));return;}
  const bankBranch=ev.target.closest('[data-bank-branch]');if(bankBranch){
    const id=Number(bankBranch.dataset.bankBranch);
    openDialog('Cuenta receptora de la sucursal',`
      ${field('Banco receptor','nombreBanco','text','Nombre del banco')}
      ${field('Titular','titular','text','Nombre o razón social')}
      ${field('CLABE de la sucursal','clabe','text','18 dígitos')}
      <div class="note">Esta cuenta recibe transferencias SPEI directas de los clientes. Si cambias la CLABE, deberá verificarse nuevamente la cuenta conectada de Stripe.</div>
    `,async d=>request('POST',`/stripe/guardar-cuenta/${id}`,{
      nombreBanco:d.get('nombreBanco'),titular:d.get('titular'),clabe:d.get('clabe')
    }));return;
  }
  const connectBranch=ev.target.closest('[data-stripe-branch]');if(connectBranch){
    (async()=>{try{const resp=await request('POST',`/stripe/conectar/${Number(connectBranch.dataset.stripeBranch)}`,{});
    if(resp?.url && /^https:\/\//.test(resp.url)){location.href=resp.url;}else throw Error('Stripe no devolvió un enlace seguro.');}
    catch(err){showAlert(err.message,true);}})();return;
  }
  const serviceBranch=ev.target.closest('[data-branch-services]');if(serviceBranch){openServiciosSucursal(Number(serviceBranch.dataset.branchServices));return;}
  const serviceEmployee=ev.target.closest('[data-employee-services]');if(serviceEmployee){openServiciosEmpleado(Number(serviceEmployee.dataset.employeeServices));return;}
  const weekBranch=ev.target.closest('[data-week-branch]');if(weekBranch){openHorarioSemana('sucursal',Number(weekBranch.dataset.weekBranch));return;}
  const weekEmployee=ev.target.closest('[data-week-employee]');if(weekEmployee){openHorarioSemana('empleado',Number(weekEmployee.dataset.weekSucursal),Number(weekEmployee.dataset.weekEmployee));return;}
  const price=ev.target.closest('[data-price]');if(price){openPrice(Number(price.dataset.price));return;}
  const cita=ev.target.closest('[data-cita]');if(cita){openCita(Number(cita.dataset.cita));return;}
  const cobro=ev.target.closest('[data-cobro]');if(cobro){openCobro(Number(cobro.dataset.cobro));return;}
  const create=ev.target.closest('[data-create]');if(create){if(create.dataset.create==='servicio')openServicioNuevo();else if(create.dataset.create==='acceso')openAccesoNuevo();else if(create.dataset.create==='personal')openPersonalNuevo();else if(create.dataset.create==='sucursal')openSucursalNueva();else if(create.dataset.create==='horarioEmpleado')openHorarioEmpleado();else if(create.dataset.create==='horarioSucursal')openHorarioSucursal();else if(create.dataset.create==='semanaEmpleado')openHorarioSemana('empleado');else if(create.dataset.create==='semanaSucursal')openHorarioSemana('sucursal');}
});
document.body.addEventListener('input',ev=>{
  if(ev.target.id==='tableSearch'){
    const value=ev.target.value;const pos=ev.target.selectionStart;
    searching=value;renderPanel();const input=$('tableSearch');if(input){input.focus();input.setSelectionRange(pos,pos);}
  }
});
start();
