using HouseholdTasks.Server.Data;
using HouseholdTasks.Server.Data.Models;
using HouseholdTasks.Shared.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Endpoints;

public static class FamilyEndpoints
{
    public static void MapFamilyEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/family-members").RequireAuthorization();

        group.MapGet("/", async (AppDbContext db) =>
        {
            var members = await db.FamilyMembers
                .OrderBy(m => m.Name)
                .Select(m => new FamilyMemberDto { Id = m.Id, Name = m.Name, Email = m.Email, IsAdmin = m.IsAdmin })
                .ToListAsync();
            return Results.Ok(members);
        });

        group.MapPost("/", async (CreateFamilyMemberDto dto, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Email))
                return Results.BadRequest("Name and email are required.");

            if (await db.FamilyMembers.AnyAsync(m => m.Email == dto.Email))
                return Results.Conflict("A family member with that email already exists.");

            var member = new FamilyMember { Name = dto.Name.Trim(), Email = dto.Email.Trim(), IsAdmin = dto.IsAdmin };
            db.FamilyMembers.Add(member);
            await db.SaveChangesAsync();

            return Results.Created($"/api/family-members/{member.Id}",
                new FamilyMemberDto { Id = member.Id, Name = member.Name, Email = member.Email, IsAdmin = member.IsAdmin });
        }).RequireAuthorization("AdminOnly");

        group.MapPut("/{id:int}", async (int id, CreateFamilyMemberDto dto, AppDbContext db) =>
        {
            var member = await db.FamilyMembers.FindAsync(id);
            if (member is null) return Results.NotFound();

            member.Name = dto.Name.Trim();
            member.IsAdmin = dto.IsAdmin;
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization("AdminOnly");

        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var member = await db.FamilyMembers.FindAsync(id);
            if (member is null) return Results.NotFound();

            db.FamilyMembers.Remove(member);
            await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization("AdminOnly");
    }
}
