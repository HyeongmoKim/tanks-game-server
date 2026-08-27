using System;

//DB형식
//players 테이블의 한 행을 서버에서 사용할 플레이어 객체로 표현
//record는 생성자 프로퍼티 자동 생성
internal sealed record Player(
    Guid PlayerId,
    string LoginId,
    int Wins,
    int Losses
);
