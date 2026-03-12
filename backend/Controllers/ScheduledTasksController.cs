using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers
{
    [ApiController]
    [Route("api/scheduled-tasks")]
    public class ScheduledTasksController : ControllerBase
    {
        private readonly MudoSoftDbContext _db;
        private readonly ILogger<ScheduledTasksController> _logger;

        public ScheduledTasksController(MudoSoftDbContext db, ILogger<ScheduledTasksController> logger)
        {
            _db = db;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetTasks()
        {
            var tasks = await _db.ScheduledTasks.OrderByDescending(t => t.CreatedAt).ToListAsync();
            return Ok(tasks);
        }

        [HttpPost]
        public async Task<IActionResult> AddTask([FromBody] CreateTaskRequest request)
        {
            _logger.LogInformation("AddTask Request: Type={Type}, Freq={Freq}, Date={Date}, Time={Time}", 
                request.Type, request.Frequency, request.TargetDate, request.TargetTime);
            if (string.IsNullOrEmpty(request.Type) || string.IsNullOrEmpty(request.Frequency))
                return BadRequest(new { error = "Type and Frequency are required." });

            var newTask = new ScheduledTask
            {
                TaskType = request.Type,
                Frequency = request.Frequency,
                TargetTime = request.TargetTime,
                TargetDate = request.TargetDate.HasValue ? request.TargetDate.Value.ToUniversalTime() : null,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // İlk NextRunTime hesaplama
            if (request.Frequency == "OneTime")
            {
                if (!request.TargetDate.HasValue)
                    return BadRequest(new { error = "TargetDate is required for OneTime tasks." });

                if (request.TargetDate.Value <= DateTime.Now)
                    return BadRequest(new { error = "TargetDate must be in the future." });

                newTask.NextRunTime = request.TargetDate.Value.ToUniversalTime();
            }
            else if (request.Frequency == "Daily")
            {
                if (!request.TargetTime.HasValue)
                    return BadRequest(new { error = "TargetTime is required for Daily tasks." });

                // Bugünün tarihi + Hedef Saat
                var todayTarget = DateTime.Today.Add(request.TargetTime.Value);

                // Eğer saat geçtiyse yarına, geçmediyse bugüne ayarla
                if (todayTarget <= DateTime.Now)
                {
                    newTask.NextRunTime = todayTarget.AddDays(1).ToUniversalTime();
                }
                else
                {
                    newTask.NextRunTime = todayTarget.ToUniversalTime();
                }
            }
            else
            {
                return BadRequest(new { error = "Invalid Frequency." });
            }

            try 
            {
                _db.ScheduledTasks.Add(newTask);
                await _db.SaveChangesAsync();

                _logger.LogInformation("New Scheduled Task added: {Id} - {NextRun}", newTask.Id, newTask.NextRunTime);
                return Ok(newTask);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding scheduled task");
                return StatusCode(500, new { error = "Internal Server Error: " + ex.Message });
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTask(int id)
        {
            var task = await _db.ScheduledTasks.FindAsync(id);
            if (task == null) return NotFound();

            _db.ScheduledTasks.Remove(task);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Scheduled Task deleted: {Id}", id);
            return Ok(new { success = true });
        }

        public class CreateTaskRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("type")]
            public string Type { get; set; } = "InboxCleanup";

            [System.Text.Json.Serialization.JsonPropertyName("frequency")]
            public string Frequency { get; set; } = "OneTime"; // OneTime, Daily

            [System.Text.Json.Serialization.JsonPropertyName("targetTime")]
            public TimeSpan? TargetTime { get; set; } // "14:30:00"

            [System.Text.Json.Serialization.JsonPropertyName("targetDate")]
            public DateTime? TargetDate { get; set; } // "2024-02-15T14:30:00"
        }
    }
}
