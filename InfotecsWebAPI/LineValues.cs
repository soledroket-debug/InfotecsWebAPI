using System.ComponentModel.DataAnnotations;

namespace InfotecsWebAPI
{
    public class LineValues
    {
        [Required(ErrorMessage = "Поле Date обязательно")]
        [Range(typeof(DateTimeOffset), "2000-01-01", "3000-01-01", ErrorMessage = "Дата должна быть не раньше 2000 года")]
        public DateTimeOffset? Date { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Date.HasValue && Date.Value > DateTime.Now)
            {
                yield return new ValidationResult("Дата не может быть из будущего", new[] { nameof(Date) });
            }
        }
        [Required(ErrorMessage = "Поле Execution time  обязательно")]
        [Range(0, int.MaxValue, ErrorMessage = $"Время выполнения не может быть меньше 0, или больше 2 147 483 647")]
        public int ExecutionTime { get; set; }

        [Required(ErrorMessage = "Поле Value обязательно")]
        [Range(0.0, double.MaxValue, ErrorMessage = "Значение показателя не может быть меньше 0, или больше 1,79 x 10^308")]
        public double Value { get; set; }

        
    }
    public class ResultRecord
    {
        public string filename { get; set; }
        public int time_delta { get; set; }
        public DateTimeOffset start_time { get; set; }
        public float avg_exec_time { get; set; }
        public double avg_value { get; set; }
        public double median_value { get; set; }
        public double max_value { get; set; }
        public double min_value { get; set; }
    }
}
