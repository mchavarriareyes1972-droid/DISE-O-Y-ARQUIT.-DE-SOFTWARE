using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Tienda_Giovanna.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSingleton(new Tienda_Giovanna.Services.TiendaService());
builder.Services.AddSingleton<UsuariosService>();
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
        var servicio = contexto.HttpContext.RequestServices.GetRequiredService<UsuariosService>();
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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

