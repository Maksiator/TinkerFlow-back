using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.API.DTOs;
using TinkerFlow.Domain.Entities;
using TinkerFlow.Domain.Enums;
using TinkerFlow.Infrastructure;

namespace TinkerFlow.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly TinkerFlowDbContext _context;

    public ProjectsController(TinkerFlowDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetProjects([FromQuery] ProjectSoftware? software = null, [FromQuery] bool? isAdvanced = null)
    {
        var query = _context.Projects.AsNoTracking().AsQueryable();

        if (software.HasValue)
        {
            query = query.Where(p => p.Software == software.Value);
        }

        if (isAdvanced.HasValue)
        {
            query = query.Where(p => p.IsAdvanced == isAdvanced.Value);
        }

        var projects = await query
            .OrderBy(p => p.Software)
            .ThenBy(p => p.IsAdvanced)
            .ThenBy(p => p.SequenceOrder)
            .ToListAsync();
        return Ok(projects);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProject(Guid id)
    {
        var project = await _context.Projects.FindAsync(id);
        if (project == null)
        {
            return NotFound(new {message = "Nie znaleziono projektu o podanym ID."});
        }
        
        return Ok(new ProjectResponse(
            project.Id, 
            project.Name, 
            project.Code, 
            project.SequenceOrder, 
            project.IsPractice, 
            project.IsYearBoundary,
            project.Software,
            project.IsAdvanced
        ));
    }
    
    [HttpPost]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> AddProject([FromBody] CreateProjectRequest request)
    {
        var exists = await _context.Projects.AnyAsync(p =>
            p.Name.ToLower() == request.Name.ToLower() ||
            p.Code.ToLower() == request.Code.ToLower());

        if (exists)
        {
            return Conflict(new {message = "Projekt o tej samej nazwie lub kodzie już istnieje."});
        }

        var newProject = new Project
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Code = request.Code,
            SequenceOrder = request.SequenceOrder,
            IsPractice = request.IsPractice,
            IsYearBoundary = request.IsYearBoundary,
            Software = request.Software,
            IsAdvanced = request.IsAdvanced
        };

        _context.Projects.Add(newProject);
        await _context.SaveChangesAsync();

        var response = new ProjectResponse(
            newProject.Id, 
            newProject.Name, 
            newProject.Code, 
            newProject.SequenceOrder, 
            newProject.IsPractice, 
            newProject.IsYearBoundary,
            newProject.Software,
            newProject.IsAdvanced
        );
        
        return CreatedAtAction(nameof(GetProject), new { id = newProject.Id }, response);
    }
    
    [HttpPost("bulk")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> AddProjectsBulk([FromBody] List<CreateProjectRequest> requests)
    {
        if (!requests.Any())
        {
            return BadRequest(new { message = "Lista projektów jest pusta." });
        }

        var newProjects = new List<Project>();
        
        var existingProjects = await _context.Projects
            .Select(p => new { p.Code, p.Name })
            .ToListAsync();

        foreach (var req in requests)
        {
            var codeTrimmed = req.Code.Trim();
            var nameTrimmed = req.Name.Trim();

            var exists = existingProjects.Any(p => 
                (!string.IsNullOrEmpty(codeTrimmed) && p.Code == codeTrimmed) || 
                (string.IsNullOrEmpty(codeTrimmed) && p.Name == nameTrimmed));

            if (!exists)
            {
                newProjects.Add(new Project
                {
                    Id = Guid.NewGuid(),
                    Name = nameTrimmed,
                    Code = codeTrimmed, 
                    SequenceOrder = req.SequenceOrder,
                    IsPractice = req.IsPractice,
                    IsYearBoundary = req.IsYearBoundary,
                    Software = req.Software,
                    IsAdvanced = req.IsAdvanced
                });
            }
        }

        if (newProjects.Any())
        {
            _context.Projects.AddRange(newProjects);
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = $"Pomyślnie dodano {newProjects.Count} nowych projektów. (Pominięto duplikaty)." });
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> UpdateProject(Guid id, [FromBody] UpdateProjectRequest request)
    {
        var existingProject = await _context.Projects.FirstOrDefaultAsync(p => p.Id == id);
        
        if (existingProject == null)
        {
            return NotFound(new { message = "Nie znaleziono projektu o podanym ID." });
        }

        existingProject.Name = request.Name;
        existingProject.Code = request.Code;
        existingProject.SequenceOrder = request.SequenceOrder;
        existingProject.IsPractice = request.IsPractice;
        existingProject.IsYearBoundary = request.IsYearBoundary;
        existingProject.Software = request.Software;
        existingProject.IsAdvanced = request.IsAdvanced;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> DeleteProject([FromRoute] Guid id)
    {
        var project = await _context.Projects.FindAsync(id);

        if (project == null)
        {
            return NotFound(new {message = "Nie znaleziono projektu o podanym ID."});
        }

        var isUsed = await _context.StudentProjects.AnyAsync(sp => sp.ProjectId == id);
        if (isUsed)
        {
            return BadRequest(new {message = "Nie można usunąć projektu, ponieważ jest przypisany do co najmniej jednego ucznia."});
        }
        
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();
        
        return NoContent();
    }
    
    [HttpGet("{id}/usage")]
    [ProducesResponseType(typeof(ProjectUsageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProjectUsage(Guid id)
    {
        // 1. Pobieramy projekt (tymczasowo do anonimowego obiektu, bo to tylko na użytek wewnętrzny metody)
        var project = await _context.Projects
            .Where(p => p.Id == id)
            .Select(p => new {
                p.Id,
                p.Name,
                p.Code
            })
            .FirstOrDefaultAsync();

        if (project == null) return NotFound(new { message = "Projekt nie istnieje." });

        // 2. Pobieramy listę uczniów i od razu mapujemy na mocno typowane DTO
        var students = await _context.StudentProjects
            .Where(sp => sp.ProjectId == id)
            .Select(sp => new ProjectUsageStudentDto(
                sp.StudentId,
                sp.Student.FirstName + " " + sp.Student.LastName,
                sp.Student.Group != null ? sp.Student.Group.Name : "Brak grupy",
                sp.Status
            ))
            .ToListAsync();

        // 3. Zwracamy piękny, udokumentowany obiekt DTO
        var response = new ProjectUsageResponse(
            project.Id,
            project.Name,
            project.Code,
            students
        );

        return Ok(response);
    }
    
    [HttpPost("merge")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> MergeProjects([FromBody] MergeProjectsRequest request)
    {
        // 1. Sprawdź czy projekty istnieją
        var sourceProject = await _context.Projects.FindAsync(request.SourceProjectId);
        var targetProject = await _context.Projects.FindAsync(request.TargetProjectId);

        if (sourceProject == null || targetProject == null)
            return NotFound(new { message = "Jeden z projektów nie istnieje." });

        // 2. Pobierz wszystkie przypisania uczniów z projektu źródłowego
        var sourceAssignments = await _context.StudentProjects
            .Where(sp => sp.ProjectId == request.SourceProjectId)
            .ToListAsync();

        foreach (var assignment in sourceAssignments)
        {
            // Sprawdź czy uczeń nie ma już wpisu w projekcie docelowym
            var existsInTarget = await _context.StudentProjects
                .AnyAsync(sp => sp.StudentId == assignment.StudentId && sp.ProjectId == request.TargetProjectId);

            if (!existsInTarget)
            {
                // Przepisujemy projekt
                assignment.ProjectId = request.TargetProjectId;
                _context.Entry(assignment).State = EntityState.Modified;
            }
            else
            {
                // Jeśli uczeń był w obu, usuwamy ten ze źródłowego (target jest ważniejszy)
                _context.StudentProjects.Remove(assignment);
            }
        }

        await _context.SaveChangesAsync();
    
        // 3. Po migracji usuwamy pusty już projekt źródłowy
        _context.Projects.Remove(sourceProject);
        await _context.SaveChangesAsync();

        return Ok(new { message = $"Pomyślnie przeniesiono uczniów i usunięto projekt {sourceProject.Name}" });
    }

    [HttpPost("reorder")]
    [Authorize(Roles = "Admin,Coordinator")]
    public async Task<IActionResult> ReorderProjects([FromBody] List<ProjectOrderItemDto> items)
    {
        if (items == null || !items.Any())
        {
            return BadRequest(new { message = "Lista zmian kolejności jest pusta." });
        }

        var ids = items.Select(i => i.Id).ToList();
        var projects = await _context.Projects.Where(p => ids.Contains(p.Id)).ToListAsync();

        var itemsDict = items.ToDictionary(i => i.Id, i => i.SequenceOrder);
        foreach (var project in projects)
        {
            if (itemsDict.TryGetValue(project.Id, out var newOrder))
            {
                project.SequenceOrder = newOrder;
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Zaktualizowano kolejność dla {projects.Count} projektów." });
    }
}

