'use strict';
(() => {
  const form = document.querySelector('[data-form]');
  if (!form) return;
  const status = document.getElementById('formStatus');
  const btn = form.querySelector('[type=submit]');
  const type = form.dataset.form;
  const byId = id => document.getElementById(id);
  const setStatus = (text, error = false) => {
    status.textContent = text;
    status.style.color = error ? '#c63443' : '#176e5c';
  };
  const errorMessage = async response => {
    try { const data = await response.json(); return data.error || data.mensaje || 'Ocurrió un error'; }
    catch { return 'No se pudo procesar la solicitud.'; }
  };
  const requestList = async uri => {
    const r = await fetch(uri, {credentials:'same-origin',headers:{Accept:'application/json'}});
    if (!r.ok) throw new Error(await errorMessage(r));
    const data = await r.json();
    if (!Array.isArray(data)) throw new Error('El servidor no devolvió el catálogo esperado.');
    return data;
  };
  const fillOptions = (select, data, placeholder) => {
    select.replaceChildren(new Option(placeholder,''));
    data.forEach(row => select.add(new Option(row.nombre_giro || row.nombre, String(row.id_giro ?? row.id))));
    select.disabled = data.length === 0;
  };
  const safeGeo = (text,error = false) => {
    const out = byId('geoStatus');
    if (out) {out.textContent = text;out.style.color = error ? '#b42332' : '#304e80';}
  };
  if (type === 'empresa') {
    const giro = byId('idGiro'), estado = byId('idEstado'), municipio = byId('idMunicipio'), colonia = byId('idColonia'), cp = byId('codigoPostal');
    requestList('/api/publico/giros').then(data => fillOptions(giro,data,'Seleccionar más adelante'))
      .catch(e => safeGeo('No fue posible cargar los giros: '+e.message,true));
    requestList('/api/publico/geo/estados').then(data => {
      fillOptions(estado,data,'Selecciona tu estado');
      if (!data.length) safeGeo('No hay estados registrados en SQL Server. Ejecuta el ajuste 03.',true);
    }).catch(e => safeGeo('Error de estados: '+e.message,true));
    estado.addEventListener('change', async () => {
      fillOptions(municipio,[],'Selecciona estado');
      fillOptions(colonia,[],'Selecciona municipio');
      cp.value = '';
      lastGeoKey = null;
      if (!estado.value) {safeGeo('Selecciona un estado para continuar.');return;}
      safeGeo('Cargando municipios…');
      try {
        const data = await requestList('/api/publico/geo/municipios?idEstado='+encodeURIComponent(estado.value));
        fillOptions(municipio,data, data.length ? 'Selecciona municipio' : 'Sin municipios en el catálogo');
        safeGeo(data.length ? 'Selecciona municipio y colonia.' : 'No hay municipios cargados para este estado. Importa los datos SEPOMEX en SQL Server.',!data.length);
      } catch (e) {safeGeo(e.message,true);}
    });
    let colonyVersion = 0;
    let lastGeoKey = null;
    const loadColonias = async () => {
      const key = municipio.value + '|' + cp.value;
      if (key === lastGeoKey) return; // no borra la colonia elegida en un segundo evento change
      lastGeoKey = key;
      const version=++colonyVersion;
      fillOptions(colonia,[], municipio.value ? 'Cargando colonias…' : 'Selecciona municipio');
      if (!municipio.value) return;
      if (cp.value && !/^\d{5}$/.test(cp.value)) {safeGeo('Escribe un código postal de 5 dígitos o déjalo vacío.',true);return;}
      try {
        const params = new URLSearchParams({idMunicipio:municipio.value});
        if (cp.value) params.set('cp',cp.value);
        const data = await requestList('/api/publico/geo/colonias?'+params);
        if(version!==colonyVersion)return;
        fillOptions(colonia,data,data.length?'Selecciona colonia / asentamiento':'Sin colonias con ese filtro');
        safeGeo(data.length ? 'Domicilio: elige la colonia y captura la calle.' : 'Sin colonias: elimina el filtro CP o importa el catálogo SEPOMEX.',!data.length);
      } catch (e) {if(version===colonyVersion)safeGeo(e.message,true);}
    };
    municipio.addEventListener('change', () => {cp.value='';lastGeoKey=null;loadColonias();});
    cp.addEventListener('change', loadColonias);
    const logo = byId('logo'), preview = byId('logoPreview');
    let previewUrl = null;
    logo.addEventListener('change', () => {
      if (previewUrl) {URL.revokeObjectURL(previewUrl);previewUrl=null;}
      preview.hidden=true;preview.replaceChildren();
      const file=logo.files?.[0];
      if(!file)return;
      if(file.size === 0 || file.size > 2097152 || !['image/png','image/jpeg','image/webp'].includes(file.type)) {
        logo.value='';setStatus('El logotipo debe ser JPG, PNG o WebP y pesar hasta 2 MiB (2,097,152 bytes).',true);return;
      }
      previewUrl=URL.createObjectURL(file);
      const img=new Image();img.src=previewUrl;img.alt='Vista previa del logotipo';
      preview.append(img);preview.hidden=false;setStatus('Logotipo listo para subir a la base de datos.');
    });
  }
  form.addEventListener('input',event=>{
    if(type==='cliente' && (event.target.id==='telefono'||event.target.id==='ladaPais')){
      const whatsapp=byId('telefonoWhatsapp');
      if(whatsapp && !whatsapp.dataset.manual)whatsapp.value=byId('ladaPais').value+byId('telefono').value.replace(/[^0-9]/g,'');
    }
    if(event.target.id==='telefonoWhatsapp')event.target.dataset.manual='1';
  });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if(!form.reportValidity())return;
    if(type==='cliente') {
      const whatsapp=byId('telefonoWhatsapp');
      if(whatsapp && byId('aceptaWhatsapp')?.checked) {
        if(!whatsapp.value.trim())whatsapp.value=byId('ladaPais').value+byId('telefono').value.trim();
        if(!/^\+[1-9][0-9]{8,14}$/.test(whatsapp.value.trim())){setStatus('Revisa la lada y el número de WhatsApp.',true);return;}
      }
    }
    if(!/^(?=.{8,128}$)(?=.*[A-Z])(?=.*[a-z])(?=.*[0-9])(?=.*[^A-Za-z0-9\s]).*$/.test(byId('contrasena').value)) {
      setStatus('La contraseña necesita 8+ caracteres, mayúscula, minúscula, número y símbolo.',true);return;
    }
    if(type==='empresa' && byId('contrasena').value!==byId('confirmarContrasena').value) {
      byId('confirmarContrasena').setCustomValidity('Las contraseñas no coinciden.');
      byId('confirmarContrasena').reportValidity();
      byId('confirmarContrasena').setCustomValidity('');return;
    }
    setStatus('Guardando información de forma segura…');btn.disabled=true;
    try {
      const csrfR=await fetch('/api/empresa/csrf',{credentials:'same-origin'});
      if(!csrfR.ok)throw Error('No fue posible obtener el token de seguridad.');
      const token=(await csrfR.json()).token;
      let body,headers={'X-CSRF-TOKEN':token};
      if(type==='empresa') {body=new FormData(form);} // multipart + imagen real, no Base64 ni URL
      else {
        const data=Object.fromEntries(new FormData(form));body=JSON.stringify(data);
        headers['Content-Type']='application/json';
      }
      const response=await fetch('/api/publico/registro/'+type,{
        method:'POST',credentials:'same-origin',headers,body
      });
      if(!response.ok)throw Error(await errorMessage(response));
      const data=await response.json();
      form.reset();
      setStatus(data.mensaje || 'Registro correcto.');
      if(type==='empresa') {
        const loginLink=document.createElement('a');loginLink.href='/empresa/index.html';
        loginLink.textContent=' Iniciar sesión como administrador de empresa →';loginLink.style.color='#1955e8';
        status.append(loginLink);
        byId('logoPreview').hidden=true;
        byId('idMunicipio').replaceChildren(new Option('Selecciona estado',''));
        byId('idMunicipio').disabled=true;
        byId('idColonia').replaceChildren(new Option('Selecciona municipio',''));
        byId('idColonia').disabled=true;
        safeGeo('Selecciona un estado para continuar.');
      }
    }catch(e){setStatus(e.message||'No se pudo completar el registro.',true);}
    finally{btn.disabled=false;}
  });
})();
