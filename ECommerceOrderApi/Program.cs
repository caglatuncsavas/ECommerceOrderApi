using ECommerceOrderApi.Data;
using ECommerceOrderApi.Data.Entities;
using ECommerceOrderApi.Services.Interfaces;
using ECommerceOrderApi.Services;
using ECommerceOrderApi.V1.Requests;
using ECommerceOrderApi.V1.Requests.Validators;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ECommerceDbContext>(options =>
    options.UseInMemoryDatabase("ECommerceDb"));

builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
{
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ECommerceDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddValidatorsFromAssemblyContaining<CreateOrderRequestValidator>();
builder.Services.AddScoped<IValidator<CreateOrderRequest>, CreateOrderRequestValidator>();

// Token Management Servisleri
builder.Services.AddScoped<ITokenService, TokenService>();

// Background Services
builder.Services.AddHostedService<TokenRenewalBackgroundService>();
builder.Services.AddHostedService<OrderSyncBackgroundService>();

builder.Services.AddHttpClient();

// TokenService için özel HttpClient (named client)
builder.Services.AddHttpClient("TokenService", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

var app = builder.Build();

// Test kullanıcısı otomatik oluştur
await CreateTestUser(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Test kullanıcısı oluşturma metodu
static async Task CreateTestUser(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();
    UserManager<ApplicationUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        string testEmail = "testuser@test.com";
        ApplicationUser? existingUser = await userManager.FindByEmailAsync(testEmail);

        if (existingUser == null)
        {
            ApplicationUser testUser = new ApplicationUser
            {
                UserName = testEmail,
                Email = testEmail,
                EmailConfirmed = true
            };

            IdentityResult result = await userManager.CreateAsync(testUser, "Test123!");

            if (result.Succeeded)
            {
                logger.LogInformation(" Test kullanıcısı oluşturuldu: {Email} - ID: {UserId}", testEmail, testUser.Id);
                logger.LogInformation(" Test için kullanın: GET /api/v1/orders?userId={UserId}", testUser.Id);
            }
            else
            {
                logger.LogError(" Test kullanıcısı oluşturulamadı: {Errors}", 
                    string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }
        else
        {
            logger.LogInformation(" Test kullanıcısı zaten mevcut: {Email} - ID: {UserId}", testEmail, existingUser.Id);
            logger.LogInformation(" Test için kullanın: GET /api/v1/orders?userId={UserId}", existingUser.Id);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, " Test kullanıcısı oluşturulurken hata");
    }
}
