using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;


namespace InfotecsWebAPI.Controllers
{
    [ApiController]
    [Route("api/")]
    public class WebAPIController : ControllerBase
    {
        private readonly string _connectionString;

        public WebAPIController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }
        [HttpGet("results/filtered")]
        public async Task<IActionResult> GetFilteredResults(
        [FromQuery] string? filenameFilter,
        [FromQuery] DateTimeOffset? startTimeFrom,
        [FromQuery] DateTimeOffset? startTimeTo,
        [FromQuery] double? avgValueMin,
        [FromQuery] double? avgValueMax,
        [FromQuery] float? avgExecTimeMin,
        [FromQuery] float? avgExecTimeMax)
        {
            var results = new List<ResultRecord>();
            var sqlBuilder = new StringBuilder("SELECT * FROM \"Results\" WHERE 1=1");
            var parameters = new List<NpgsqlParameter>();
            // Фильтр по имени: если пустой, условие просто не добавится в запрос
            if (!string.IsNullOrWhiteSpace(filenameFilter))
            {
                sqlBuilder.Append(" AND filename ILIKE @filenameFilter");
                parameters.Add(new NpgsqlParameter("@filenameFilter", $"%{filenameFilter}%"));
            }
            // Фильтры по времени
            if (startTimeFrom.HasValue)
            {
                sqlBuilder.Append(" AND start_time >= @startTimeFrom");
                parameters.Add(new NpgsqlParameter("@startTimeFrom", startTimeFrom.Value));
            }
            if (startTimeTo.HasValue)
            {
                sqlBuilder.Append(" AND start_time <= @startTimeTo");
                parameters.Add(new NpgsqlParameter("@startTimeTo", startTimeTo.Value));
            }
            // Фильтры по значениям
            if (avgValueMin.HasValue)
            {
                sqlBuilder.Append(" AND avg_value >= @avgValueMin");
                parameters.Add(new NpgsqlParameter("@avgValueMin", avgValueMin.Value));
            }
            if (avgValueMax.HasValue)
            {
                sqlBuilder.Append(" AND avg_value <= @avgValueMax");
                parameters.Add(new NpgsqlParameter("@avgValueMax", avgValueMax.Value));
            }
            // Фильтры по времени выполнения
            if (avgExecTimeMin.HasValue)
            {
                sqlBuilder.Append(" AND avg_exec_time >= @avgExecTimeMin");
                parameters.Add(new NpgsqlParameter("@avgExecTimeMin", avgExecTimeMin.Value));
            }
            if (avgExecTimeMax.HasValue)
            {
                sqlBuilder.Append(" AND avg_exec_time <= @avgExecTimeMax");
                parameters.Add(new NpgsqlParameter("@avgExecTimeMax", avgExecTimeMax.Value));
            }
            // Выполняем запрос по фильтрам
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var cmd = new NpgsqlCommand(sqlBuilder.ToString(), connection);
                cmd.Parameters.AddRange(parameters.ToArray());

                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new ResultRecord
                    {
                        filename = reader.GetString(0),
                        time_delta = reader.GetInt32(1),
                        start_time = reader.GetFieldValue<DateTimeOffset>(2),
                        avg_exec_time = reader.GetFloat(3),
                        avg_value = reader.GetDouble(4),
                        median_value = reader.GetDouble(5),
                        max_value = reader.GetDouble(6),
                        min_value = reader.GetDouble(7)
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Ошибка получения данных: {ex.Message}");
            }
            return Ok(results);
        }
        // простой ping чтоб проверить подключение к БД
        [HttpGet("ping")]
        public async Task<IActionResult> Ping()
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                const string sql = @"SELECT count(*) FROM public.""Values""";
                await using (var cmd = new NpgsqlCommand(sql, connection))
                {
                    var totalRows = await cmd.ExecuteScalarAsync();
                    // Вывод в консоль сервера
                    Console.WriteLine($"[PING] Проверка связи с БД. Всего строк в Values: {totalRows}");
                    // Ответ клиенту
                    return Ok(new
                    {
                        status = "Healthy",
                        db_connection = "Connected",
                        total_records = totalRows
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PING ERROR] Ошибка: {ex.Message}");
                return StatusCode(500, $"Ошибка подключения к БД: {ex.Message}");
            }
        }
        [HttpPost("values/upload-csv")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadCsv(IFormFile file)
        {
            // 1. Базовая проверка файла
            if (file == null || file.Length == 0) return BadRequest("Файл не выбран");
            var metrics = new List<LineValues>();
            using var reader = new StreamReader(file.OpenReadStream());

            int lineNum = 0; // Начинаем с 0 строки
            bool isFirstLine = true;
            //для чтения указанного в тз формата ГГГГ-ММ-ДДTчч-мм-сс.ммммZ
            string[] formats = {
                "yyyy-MM-ddTHH-mm-ss.ffffK",      // K обработает и Z, и смещения
                "yyyy-MM-ddTHH-mm-ss.ffffzzz",    // Для явных смещений +03:00
                "yyyy-MM-ddTHH-mm-ss.ffff'Z'"     // Для явного Z
                };

            while (await reader.ReadLineAsync() is { } line)
            {
                lineNum++;
                var values = line.Split(';');
                // Пропуск пустых строк
                if (values.Length < 3) continue;
                // 1. Проверка на заголовок       
                if (isFirstLine)
                {
                    isFirstLine = false;
                    // Пробуем распарсить первую колонку как дату. 
                    // Если не получается — считаем эту строку заголовком и пропускаем.
                    bool isDate = DateTimeOffset.TryParseExact(values[0], formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);

                    if (!isDate)
                    {
                        Console.WriteLine("Обнаружен и пропущен заголовок");
                        continue;
                    }
                }
                // 2. Валидация атрибутов ([Required], [ValidDateRange] и т.д.)
                var row = new LineValues
                {
                    Date = DateTimeOffset.TryParseExact(values[0], formats,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var d) ? (DateTimeOffset?)d.ToUniversalTime() : null, // null чтобы потом легко отсеить в LineVaules
                    ExecutionTime = int.TryParse(values[1], out var e) ? e : -1, // -1 чтобы потом легко отсеить в LineVaules
                    Value = double.TryParse(values[2].Replace(",", "."), CultureInfo.InvariantCulture, out var v) ? v : -1.0 // -1.0 чтобы потом легко отсеить в LineVaules
                };
                Console.WriteLine($"[Строка {lineNum - 1,-5} | Дата: {d:dd.MM.yyyy HH:mm:ss} | {e,4} сек | {v,8:F2} | Файл: {file.FileName}");
                var validationResults = new List<ValidationResult>();
                if (!Validator.TryValidateObject(row, new ValidationContext(row), validationResults, true))
                {
                    return BadRequest(new { error = $"Ошибка в строке {lineNum}", details = validationResults });
                }
                metrics.Add(row);
            }
            // 3. Валидация количества строк
            if (metrics.Count == 0 || metrics.Count > 10000)
            {
                return BadRequest("Количество строк должно быть от 1 до 10 000. " + metrics.Count);
            }
            // 4. Данные для таблрицы Result
            DateTimeOffset? minDate = metrics.Min(x => x.Date.Value);
            DateTimeOffset? maxDate = metrics.Max(x => x.Date.Value);//нужно для timeRangeSeconds
            double timeRangeSeconds = (maxDate - minDate).Value.TotalSeconds;
            double avgExecutionTime = metrics.Average(x => x.ExecutionTime);
            var sortedValues = metrics.Select(x => x.Value).OrderBy(v => v).ToList();//нужно для медианы значений
            double minValue = metrics.Min(x => x.Value);
            double maxValue = metrics.Max(x => x.Value);
            double avgValue = metrics.Average(x => x.Value);
            double medianValue = sortedValues[sortedValues.Count / 2];
            // 5. Сохранение в таблицу Values
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            try
            {
                await using (var writer = connection.BeginBinaryImport(@"COPY public.""Values"" (event_date, execution_time, event_value, filename) FROM STDIN (FORMAT BINARY)"))
                {
                    foreach (var row in metrics)
                    {
                        await writer.StartRowAsync();
                        await writer.WriteAsync(row.Date, NpgsqlTypes.NpgsqlDbType.TimestampTz);
                        await writer.WriteAsync(row.ExecutionTime, NpgsqlTypes.NpgsqlDbType.Integer);
                        await writer.WriteAsync(row.Value, NpgsqlTypes.NpgsqlDbType.Double);
                        await writer.WriteAsync(file.FileName, NpgsqlTypes.NpgsqlDbType.Text);
                    }
                    await writer.CompleteAsync();
                }
                //6.Сохранение в таблицу Results (с заменой в слуячае совпадения)
                string sql = @"
                INSERT INTO public.""Results"" 
                (filename, time_delta, start_time, avg_exec_time, avg_value, median_value, max_value, min_value) 
                VALUES (@f, @td, @st, @aet, @av, @mv, @maxv, @minv)
                ON CONFLICT (filename) 
                DO UPDATE SET 
                    time_delta = EXCLUDED.time_delta,
                    start_time = EXCLUDED.start_time,
                    avg_exec_time = EXCLUDED.avg_exec_time,
                    avg_value = EXCLUDED.avg_value,
                    median_value = EXCLUDED.median_value,
                    max_value = EXCLUDED.max_value,
                    min_value = EXCLUDED.min_value;";

                await using (var cmd = new NpgsqlCommand(sql, connection, transaction))
                {
                    cmd.Parameters.AddWithValue("f", file.FileName);
                    cmd.Parameters.AddWithValue("td", (int)timeRangeSeconds);
                    cmd.Parameters.AddWithValue("st", minDate);
                    cmd.Parameters.AddWithValue("aet", (float)avgExecutionTime);
                    cmd.Parameters.AddWithValue("av", avgValue);
                    cmd.Parameters.AddWithValue("mv", medianValue);
                    cmd.Parameters.AddWithValue("maxv", maxValue);
                    cmd.Parameters.AddWithValue("minv", minValue);

                    await cmd.ExecuteNonQueryAsync();
                };

                // 7. Подтверждение иезменений в БД
                await transaction.CommitAsync();
                return Ok(new { message = "Успешно!", count = metrics.Count });                
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return BadRequest($"Ошибка вставки: {ex.Message}");
            }
            
        }
        [HttpGet("results/latest-by-filename")]
        public async Task<IActionResult> GetLatestResultsByFilename(
        [FromQuery] string filename)
        {
            // 1. Вноситься название файла "file.csv"
            if (string.IsNullOrWhiteSpace(filename))
            {
                return BadRequest("Необходимо указать имя файла (filename).");
            }
            var results = new List<LineValues>();
            var sql = @"
            SELECT event_date, execution_time, event_value 
            FROM ""Values"" 
            WHERE filename ILIKE @filename 
            ORDER BY event_date DESC 
            LIMIT 10";
            // 2. выборка 10 записей что индексируються в БД по имени файла
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();
                await using var cmd = new NpgsqlCommand(sql, connection);
                cmd.Parameters.Add(new NpgsqlParameter("@filename", $"%{filename}%"));
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(new LineValues
                    {
                        Date = reader.GetFieldValue<DateTimeOffset>(0), // event_date (timestamp with time zone)
                        ExecutionTime = reader.GetInt32(1),            // execution_time (integer)
                        Value = reader.GetDouble(2)                   // event_value (double precision)
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB ERROR] Ошибка: {ex.Message}");
                return StatusCode(500, $"Ошибка получения данных: {ex.Message}");
            }
            return Ok(results);
        }
    }
}
