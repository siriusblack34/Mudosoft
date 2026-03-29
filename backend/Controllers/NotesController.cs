using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Controllers
{
    public class NoteDto
    {
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
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
            return await _db.Notes.OrderByDescending(n => n.UpdatedAt).ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Note>> GetNote(Guid id)
        {
            var note = await _db.Notes.FindAsync(id);
            if (note == null) return NotFound();
            return note;
        }

        [HttpPost]
        public async Task<ActionResult<Note>> CreateNote([FromBody] NoteDto dto)
        {
            var note = new Note
            {
                Id = Guid.NewGuid(),
                Title = dto.Title,
                Content = dto.Content,
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
            var existingNote = await _db.Notes.FindAsync(id);
            if (existingNote == null) return NotFound();

            existingNote.Title = dto.Title;
            existingNote.Content = dto.Content;
            existingNote.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(existingNote);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteNote(Guid id)
        {
            var note = await _db.Notes.FindAsync(id);
            if (note == null) return NotFound();

            _db.Notes.Remove(note);
            await _db.SaveChangesAsync();

            return NoContent();
        }
    }
}
