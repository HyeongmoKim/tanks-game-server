using System;
using Npgsql;

//서버 내부에서 PostgreSQL 연결 생성을 담당하는 정적 도우미 클래스
internal static class Database
{
    ////연결 문자열을 직접 저장하지 않고, 연결 문자열이 들어 있는 환경 변수 이름만 보관
    private const string ConnectionStringEnvironmentVariable = 
    "TANKS_DB_CONNECTION_STRING";
    
    //환경 변수의 연결 문자열로 PostgreSQL 연결 풀을 관리할 DataSource 생성
    public static NpgsqlDataSource CreateDataSource()
    {
        //운영체제에 설정된 데이터베이스 연결 문자열 읽기
        string? connectionString =
        Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable
        );
        //연결 문자열이 없으면 잘못된 상태로 서버를 실행하지 않고 즉시 예외 발생
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"환경변수 {ConnectionStringEnvironmentVariable}가 설정되지 않았습니다."
            );
        }
        //필요할 때 데이터베이스 연결을 빌려주고 재사용하는 NpgsqlDataSource 반환
        return NpgsqlDataSource.Create(connectionString);
    }
}
