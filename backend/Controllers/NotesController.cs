using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using System.Security.Claims;

namespace MudoSoft.Backend.Controllers
{
    public class NoteDto
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsShared { get; set; }
    }

    [ApiController]
    [Authorize]
    [Route("api/notes")]
    public class NotesController : ControllerBase
    {
        private readonly MudoSoftDbContext _db;

        public NotesController(MudoSoftDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Note>>> GetNotes()
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            return await _db.Notes
                .Where(n => n.OwnerUsername == username || n.IsShared)
                .OrderByDescending(n => n.UpdatedAt)
                .ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Note>> GetNote(Guid id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var note = await _db.Notes.FindAsync(id);
            if (note == null) return NotFound();
            if (!CanAccess(note, username)) return Forbid();

            return note;
        }

        [HttpPost]
        public async Task<ActionResult<Note>> CreateNote([FromBody] NoteDto dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var note = new Note
            {
                Id = Guid.NewGuid(),
                OwnerUsername = username,
                Title = dto.Title,
                Content = dto.Content,
                IsShared = dto.IsShared,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _db.Notes.Add(note);
            await _db.SaveChangesAsync();

            return CreatedAtAction(nameof(GetNote), new { id = note.Id }, note);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateNote(Guid id, [FromBody] NoteDto dto)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var existingNote = await _db.Notes.FindAsync(id);
            if (existingNote == null) return NotFound();
            if (!CanEdit(existingNote, username)) return Forbid();

            existingNote.Title = dto.Title;
            existingNote.Content = dto.Content;
            existingNote.IsShared = dto.IsShared;
            existingNote.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(existingNote);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNote(Guid id)
        {
            var username = GetCurrentUsername();
            if (string.IsNullOrWhiteSpace(username)) return Unauthorized();

            var note = await _db.Notes.FindAsync(id);
            if (note == null) return NotFound();
            if (!CanEdit(note, username)) return Forbid();

            _db.Notes.Remove(note);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        private string? GetCurrentUsername()
        {
            return User.FindFirstValue(ClaimTypes.Name)?.Trim().ToLowerInvariant();
        }

        private static bool CanAccess(Note note, string username)
        {
            return note.OwnerUsername == username || note.IsShared;
        }

        private static bool CanEdit(Note note, string username)
        {
            return note.OwnerUsername == username;
        }
    }
}
