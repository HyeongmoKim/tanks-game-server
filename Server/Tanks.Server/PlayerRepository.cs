using Npgsql;
using NpgsqlTypes;

//플레이어 생성, 조회, 전적 갱신 등 데이터베이스 작업을 담당
internal sealed class PlayerRepository
{
    //PostgreSQL 연결 풀에서 명령과 연결을 생성하는 DataSource
    private readonly NpgsqlDataSource _dataSource;

    public PlayerRepository(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;

    }

    //로그인 id 플레이어 조회 없으면 새로 생성
    public async Task<Player> GetOrCreateAsync(string loginId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loginId);

        loginId=loginId.Trim();

        //ID가 없으면 INSERT하고, 이미 존재하면 기존 행을 반환하는 UPSERT
        const string sql = """
        INSERT INTO players (login_id)
        VALUES($1)
        ON CONFLICT (login_id)
        DO UPDATE SET login_id = EXCLUDED.login_id
        RETURNING player_id,login_id,wins,losses;
        """;

        //사용이 끝난 데이터베이스 명령 객체를 비동기로 자동 정리
        await using NpgsqlCommand command =
            _dataSource.CreateCommand(sql);
        
        //$1에 로그인 ID를 매개변수로 전달하여 SQL 인젝션과 문자열 오류 방지
        command.Parameters.AddWithValue(loginId);

        await using NpgsqlDataReader reader =
            await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException(
                "플레이어 정보를 가져오지 못했습니다.");
        }

        return new Player(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt32(3));
    }
    public async Task RecordMatchResultAsync(string winnerLoginId,IReadOnlyCollection<string> loserLoginIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(winnerLoginId);
        ArgumentNullException.ThrowIfNull(loserLoginIds);
        if(loserLoginIds.Count is <1 or > 3)
        {
            throw new ArgumentException(
                "패자는 1명 이상 3명 이하",
                nameof(loserLoginIds)
            );
        }
        if (loserLoginIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "패자의 로그인 ID가 비어있음",
                nameof(loserLoginIds));
        }
        string winner = winnerLoginId.Trim();
        string[] losers = loserLoginIds.Select(loginId=>loginId.Trim()).ToArray();
        if (losers.Any(loginId =>
                string.Equals(
                    loginId,
                    winner,
                    StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "승자가 패자 목록에 포함되어있음",
                nameof(loserLoginIds));
        }

        if (losers.Distinct(StringComparer.Ordinal).Count()
            != losers.Length)
        {
            throw new ArgumentException(
                "패자 목록에 중복된 로그인 ID가 있음",
                nameof(loserLoginIds));
        }
        //연결 풀에서 PostgreSQL 연결을 빌림
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        try
        {
            await using NpgsqlCommand winnerCommand = connection.CreateCommand();
            winnerCommand.Transaction = transaction;
            winnerCommand.CommandText = """
                UPDATE players
                SET wins = wins + 1
                WHERE login_id=$1;
            """;
            winnerCommand.Parameters.AddWithValue(winner);
            //정확히 한 행이 갱신됐는지 확인하여 승자 계정의 존재 여부 검사
            int updateWinnerCount = await winnerCommand.ExecuteNonQueryAsync();
            if (updateWinnerCount != 1)
            {
                throw new InvalidOperationException("승자 정보 못찾음");
            }
            await using NpgsqlCommand loserCommand = connection.CreateCommand();

            loserCommand.Transaction = transaction;
            loserCommand.CommandText = """
                UPDATE players
                SET losses = losses + 1
                WHERE login_id = ANY($1);
                """;
            loserCommand.Parameters.AddWithValue(
                NpgsqlDbType.Array|NpgsqlDbType.Text,losers);
            int updateLoserCount = await loserCommand.ExecuteNonQueryAsync();
            if (updateLoserCount != losers.Length)
            {
                throw new InvalidOperationException(
                "패자의 정보를 찾지 못함");
            }
            await transaction.CommitAsync();

        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
