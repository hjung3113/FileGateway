// tests/FileGateway.IntegrationTests/DatabaseFixture.cs
using Testcontainers.MsSql;

namespace FileGateway.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    // latest 금지: 실행 시점 최신 CU 태그로 고정(확정 결정 17).
    // Testcontainers 4.x는 parameterless MsSqlBuilder()를 obsolete로 표시하므로 image 생성자 사용.
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU17-ubuntu-22.04").Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // 테스트 host cwd는 bin 출력 디렉터리이므로 csproj Content 복사본을 BaseDirectory에서 읽는다.
        await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "mvp-schema.sql")));
        await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "mvp-stored-procedure.sql")));
        await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "mvp-stored-procedure-diagnostics.sql")));
    }

    public async Task ExecuteAsync(string sql) // GO 없는 배치 실행 헬퍼
    {
        // 반환 Task 완료 전 using dispose가 연결을 끊지 않도록 async/await로 실행한다.
        await using var conn = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
