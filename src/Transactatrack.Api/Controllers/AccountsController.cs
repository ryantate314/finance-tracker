using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Application.Accounts;
using Transactatrack.Domain.Entities;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AccountsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AccountDto>>> List()
    {
        var accounts = await _db.Accounts
            .OrderBy(a => a.Name)
            .Select(a => new AccountDto(a.Id, a.FamilyId, a.OwnerId, a.Name, a.Institution, a.AccountType, a.BankCode, a.CreatedUtc))
            .ToListAsync();
        return Ok(accounts);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<AccountDto>> Get(Guid id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account is null) return NotFound();
        return Ok(new AccountDto(account.Id, account.FamilyId, account.OwnerId, account.Name, account.Institution, account.AccountType, account.BankCode, account.CreatedUtc));
    }

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountRequest request)
    {
        var ownerExists = await _db.Owners.AnyAsync(o => o.Id == request.OwnerId);
        if (!ownerExists)
            return BadRequest(new { title = "Owner not found in active family", status = 400 });

        var account = new Account
        {
            OwnerId = request.OwnerId,
            Name = request.Name,
            Institution = request.Institution,
            AccountType = request.AccountType,
            BankCode = request.BankCode
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();
        var dto = new AccountDto(account.Id, account.FamilyId, account.OwnerId, account.Name, account.Institution, account.AccountType, account.BankCode, account.CreatedUtc);
        return CreatedAtAction(nameof(Get), new { id = account.Id }, dto);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateAccountRequest request)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account is null) return NotFound();

        var ownerExists = await _db.Owners.AnyAsync(o => o.Id == request.OwnerId);
        if (!ownerExists)
            return BadRequest(new { title = "Owner not found in active family", status = 400 });

        account.OwnerId = request.OwnerId;
        account.Name = request.Name;
        account.Institution = request.Institution;
        account.AccountType = request.AccountType;
        account.BankCode = request.BankCode;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var account = await _db.Accounts.FindAsync(id);
        if (account is null) return NotFound();
        _db.Accounts.Remove(account);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { title = "Account has dependent records", status = 409 });
        }
        return NoContent();
    }
}
