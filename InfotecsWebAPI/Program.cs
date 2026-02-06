using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        // Связываем Swagger UI с эндпоинтом .NET 9
        options.SwaggerEndpoint("/openapi/v1.json", "v1");

        // Опционально: делает Swagger главной страницей (доступ по адресу /)
        options.RoutePrefix = string.Empty;
    });
}
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

try
{
    using var conn = new NpgsqlConnection(connectionString);
    conn.Open();
    Console.WriteLine("--- УСПЕХ: Подключение к PostgreSQL установлено! ---");
}
catch (Exception ex)
{
    Console.WriteLine("--- ОШИБКА: Не удалось подключиться к базе данных! ---");
    Console.WriteLine(ex.Message);
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

