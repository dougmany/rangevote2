using Dapper;
using Microsoft.Data.Sqlite;
using BCrypt.Net;

namespace RangeVote2.Data
{
    public interface IAuthenticationService
    {
        Task<ApplicationUser?> AuthenticateAsync(string email, string password);
        Task<ApplicationUser?> RegisterAsync(RegisterModel model);
        Task<ApplicationUser?> GetUserByIdAsync(Guid userId);
        Task<ApplicationUser?> GetUserByEmailAsync(string email);
        Task UpdateLastLoginAsync(Guid userId);
    }

    public class AuthenticationService : IAuthenticationService
    {
        private readonly ApplicationConfig _config;

        public AuthenticationService(ApplicationConfig config)
        {
            _config = config;
        }

        public async Task<ApplicationUser?> AuthenticateAsync(string email, string password)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            var user = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Email = @Email",
                new { Email = email }
            );

            if (user == null)
                return null;

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
                return null;

            await UpdateLastLoginAsync(user.Id);

            return user;
        }

        public async Task<ApplicationUser?> RegisterAsync(RegisterModel model)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            // Check if user already exists
            var existingUser = await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Email = @Email",
                new { Email = model.Email }
            );

            if (existingUser != null)
                return null;

            var userId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

            Guid? organizationId = null;

            // Create organization if provided
            if (!string.IsNullOrWhiteSpace(model.OrganizationName))
            {
                organizationId = Guid.NewGuid();
                await connection.ExecuteAsync(
                    @"INSERT INTO organizations (Id, Name, OwnerId, CreatedAt)
                      VALUES (@Id, @Name, @OwnerId, @CreatedAt)",
                    new
                    {
                        Id = organizationId.ToString(),
                        Name = model.OrganizationName,
                        OwnerId = userId.ToString(),
                        CreatedAt = now
                    }
                );

                // Add user as owner in organization_members
                await connection.ExecuteAsync(
                    @"INSERT INTO organization_members (Id, OrganizationId, UserId, Role, JoinedAt)
                      VALUES (@Id, @OrganizationId, @UserId, @Role, @JoinedAt)",
                    new
                    {
                        Id = Guid.NewGuid().ToString(),
                        OrganizationId = organizationId.ToString(),
                        UserId = userId.ToString(),
                        Role = "Owner",
                        JoinedAt = now
                    }
                );
            }

            // Create user
            await connection.ExecuteAsync(
                @"INSERT INTO users (Id, Email, PasswordHash, DisplayName, OrganizationId, CreatedAt, LastLoginAt)
                  VALUES (@Id, @Email, @PasswordHash, @DisplayName, @OrganizationId, @CreatedAt, @LastLoginAt)",
                new
                {
                    Id = userId.ToString(),
                    Email = model.Email,
                    PasswordHash = passwordHash,
                    DisplayName = model.DisplayName,
                    OrganizationId = organizationId?.ToString(),
                    CreatedAt = now,
                    LastLoginAt = now
                }
            );

            return await GetUserByIdAsync(userId);
        }

        public async Task<ApplicationUser?> GetUserByIdAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            return await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Id = @Id",
                new { Id = userId }
            );
        }

        public async Task<ApplicationUser?> GetUserByEmailAsync(string email)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            return await connection.QueryFirstOrDefaultAsync<ApplicationUser>(
                "SELECT * FROM users WHERE Email = @Email",
                new { Email = email }
            );
        }

        public async Task UpdateLastLoginAsync(Guid userId)
        {
            using var connection = new SqliteConnection(_config.DatabaseName);

            await connection.ExecuteAsync(
                "UPDATE users SET LastLoginAt = @LastLoginAt WHERE Id = @Id",
                new { Id = userId, LastLoginAt = DateTime.UtcNow }
            );
        }
    }
}
