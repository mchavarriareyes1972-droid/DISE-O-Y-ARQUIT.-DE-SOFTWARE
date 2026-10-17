using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Tienda_Giovanna.Services;
using Tienda_Giovanna.Datos;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
string persistenciaUsuarios = builder.Configuration["Persistencia:Usuarios"] ?? "SqlServer";
string persistenciaClientes = builder.Configuration["Persistencia:Clientes"] ?? "SqlServer";
string persistenciaProveedores = builder.Configuration["Persistencia:Proveedores"] ?? "SqlServer";
if (new[] { persistenciaUsuarios, persistenciaClientes, persistenciaProveedores }.Any(modo => modo is not ("SqlServer" or "Memoria")))
    throw new InvalidOperationException("Persistencia:Usuarios, Clientes y Proveedores deben ser SqlServer o Memoria.");
if (new[] { persistenciaUsuarios, persistenciaClientes, persistenciaProveedores }.Contains("SqlServer"))
{
    ConexionBD.Instancia.Configurar(builder.Configuration.GetConnectionString("BodegaGiovanna") ?? "");
    builder.Services.AddSingleton(ConexionBD.Instancia);
}
if (persistenciaUsuarios == "SqlServer")
    builder.Services.AddSingleton<IUsuariosService, UsuariosSqlService>();
else
    builder.Services.AddSingleton<IUsuariosService, UsuariosMemoriaService>();
if (persistenciaClientes == "SqlServer")
    builder.Services.AddSingleton<ClientesSqlService>();
if (persistenciaProveedores == "SqlServer")
    builder.Services.AddSingleton<ProveedoresSqlService>();
builder.Services.AddSingleton(sp => new TiendaService(sp.GetService<ClientesSqlService>(), sp.GetService<ProveedoresSqlService>()));
builder.Services.AddSingleton<ExportacionService>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(opciones =>
{
    opciones.LoginPath = "/Cuenta/Login";
    opciones.AccessDeniedPath = "/Cuenta/AccesoDenegado";
    opciones.ExpireTimeSpan = TimeSpan.FromHours(2);
    opciones.Cookie.HttpOnly = true;
    opciones.Cookie.SameSite = SameSiteMode.Lax;
    opciones.Events.OnRedirectToLogin = contexto =>
    {
        if (contexto.Request.Path.StartsWithSegments("/api")) contexto.Response.StatusCode = 401;
        else contexto.Response.Redirect(contexto.RedirectUri);
        return Task.CompletedTask;
    };
    opciones.Events.OnRedirectToAccessDenied = contexto =>
    {
        if (contexto.Request.Path.StartsWithSegments("/api")) contexto.Response.StatusCode = 403;
        else contexto.Response.Redirect(contexto.RedirectUri);
        return Task.CompletedTask;
    };
    opciones.Events.OnValidatePrincipal = async contexto =>
    {
        var servicio = contexto.HttpContext.RequestServices.GetRequiredService<IUsuariosService>();
        if (!int.TryParse(contexto.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out int id)
            || !int.TryParse(contexto.Principal?.FindFirstValue("Version"), out int version)
            || contexto.Principal?.FindFirstValue("Instancia") != servicio.Instancia || !servicio.ValidarSesion(id, version))
        {
            contexto.RejectPrincipal();
            await contexto.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Un fallo de SQL no habilita cuentas en memoria ni expone el error tecnico.
app.Use(async (contexto, siguiente) =>
{
    try { await siguiente(); }
    catch (Microsoft.Data.SqlClient.SqlException error)
    {
        app.Logger.LogError(error, "No se pudo consultar SQL Server.");
        if (contexto.Response.HasStarted) throw;
        contexto.Response.Clear();
        contexto.Response.StatusCode = 503;
        if (contexto.Request.Path.StartsWithSegments("/api"))
            await contexto.Response.WriteAsJsonAsync(new { mensaje = "La base de datos no esta disponible. Intenta nuevamente cuando se restablezca la conexion." });
        else
        {
            contexto.Response.ContentType = "text/plain; charset=utf-8";
            await contexto.Response.WriteAsync("La base de datos no esta disponible. Comprueba que SQL Server este iniciado y vuelve a intentar.");
        }
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

