using Citicio.Data;
using Citicio.Models;
using Microsoft.Data.SqlClient;

namespace Citicio.Services;
public sealed class BookingService(SqlStore db) {
    public async Task<List<DbRow>> DisponibilidadAsync(int sucursal,int servicio,DateTime fecha,TimeSpan hora) {
        await using var cn=await db.OpenAsync();
        if(!await SuscripcionAccesoService.PermitidoAsync(cn,null,sucursal,servicio))
            return new List<DbRow>();
        return await SqlStore.ReadAsync(cn,null,"dbo.sp_citas_consultar_disponibilidad",SqlStore.P(
            ("id_sucursal",sucursal),("id_servicio",servicio),("fecha",fecha.Date),("hora_inicio",hora)));
    }
    public async Task<int> CrearAsync(int cliente,ReservaForm f) {
        var comienzo=f.Fecha.Date+f.Hora;
        if(comienzo<=MexicoClock.Now || comienzo>MexicoClock.Now.AddDays(60))
            throw new InvalidOperationException("Selecciona un horario futuro dentro de los próximos 60 días.");
        await using var cn=await db.OpenAsync();
        await using var tx=(SqlTransaction)await cn.BeginTransactionAsync();
        try {
            if(!await SuscripcionAccesoService.PermitidoAsync(cn,tx,f.SucursalId,f.ServicioId))
                throw new InvalidOperationException("La empresa no tiene una mensualidad de CITICIO pagada y vigente para recibir nuevas citas.");
            var sv=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_citicio_lecturas",SqlStore.P(
                ("accion","servicio_horario"),("id1",f.SucursalId),("id2",f.ServicioId)))).FirstOrDefault();
            if(sv==null) throw new InvalidOperationException("El servicio no está disponible en esa sucursal.");
            var fin=comienzo.AddMinutes(sv.I("duracion_minutos"));
            if(fin.Date!=comienzo.Date) throw new InvalidOperationException("La cita debe terminar el mismo día.");
            var available=await SqlStore.ReadAsync(cn,tx,"dbo.sp_citas_consultar_disponibilidad",SqlStore.P(
                ("id_sucursal",f.SucursalId),("id_servicio",f.ServicioId),("fecha",f.Fecha.Date),("hora_inicio",f.Hora)));
            if(!available.Any(r=>r.I("id_empleado")==f.EmpleadoId)) throw new InvalidOperationException("El empleado ya no está disponible en ese horario.");
            var cita=(await SqlStore.ReadAsync(cn,tx,"dbo.sp_citas",SqlStore.P(("accion","insertar"),
                ("id_cliente",cliente),("id_sucursal",f.SucursalId),("id_empleado",f.EmpleadoId),
                ("fecha",f.Fecha.Date),("hora_inicio",comienzo),("hora_fin",fin),("observaciones",f.Observaciones)))).First();
            var id=cita.I("id_cita");
            await SqlStore.ReadAsync(cn,tx,"dbo.sp_detalles_citas",SqlStore.P(("accion","insertar"),
                ("id_cita",id),("id_servicio",f.ServicioId)));
            await tx.CommitAsync();
            return id;
        } catch {await tx.RollbackAsync();throw;}
    }
}
