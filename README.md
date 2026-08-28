# Tanks Game Server

Unity Tanks 튜토리얼을 온라인 멀티플레이어 게임으로 확장한 프로젝트

.NET 비동기 TCP 서버와 PostgreSQL을 구현하고, Terraform과 Kubernetes를 이용해 AWS EKS에 배포한 뒤 외부 Network Load Balancer를 통해 부하 테스트를 수행했습니다.

[서버 상세 문서](./docs/server.md) · [인프라 상세 문서](./docs/infrastructure.md) · [부하 테스트 결과](./docs/load-test-results.md)

---

## 프로젝트 개요

클라이언트의 기본 게임플레이와 아트 리소스는 Unity Learn의 [Tanks: Make a battle game for web and mobile](https://learn.unity.com/course/tanks-make-a-battle-game-for-web-and-mobile?uv=6) 튜토리얼을 기반으로 한다.

튜토리얼의 로컬 2인 플레이를 그대로 사용하지 않고, 독립 실행형 서버를 중심으로 로그인, 로비, 방, 채팅, 전투 동기화와 전적 저장 기능을 직접 추가했다. 완성한 서버는 컨테이너 이미지로 빌드해 AWS의 관리형 Kubernetes 환경인 EKS에 배포했습니다.

| 구분 | 내용 |
|---|---|
| 튜토리얼 기반 | 탱크 이동과 발사, 포탄, 체력, 카메라, 맵, 프리팹, 오디오와 아트 리소스 |
| 직접 구현 | TCP 클라이언트와 서버, JSON 프로토콜, 로그인, 로비, 방, 채팅, 전투 동기화와 PostgreSQL 전적 저장 |
| 인프라 구현 | Docker, Terraform, Amazon EKS, ECR, EBS, NLB, GitHub Actions와 CloudWatch Logs |
| 성능 검증 | 외부 NLB를 통한 최대 1,000개 동시 TCP 연결 부하 테스트 |

---

## 주요 기능

| 영역 | 구현 내용 |
|---|---|
| 네트워크 | TCP 연결, JSON Lines 프레이밍, 비동기 송수신과 세션 관리 |
| 로그인 | 고유 로그인 ID 검증, 플레이어 조회·생성과 전적 반환 |
| 로비 | 접속자 목록, 방 목록, 방 생성·입장·퇴장 |
| 채팅 | 로비 및 방 단위 메시지 전달 |
| 전투 | 이동, 발사, 체력, 사망과 경기 종료 상태 동기화 |
| 데이터 | PostgreSQL에 플레이어 승리·패배 전적 저장 |
| 안정성 | 잘못된 메시지 격리, 연결 종료 정리, DB 연결 재시도, Kubernetes 상태 프로브 |
| 배포 | GitHub OIDC 기반 이미지 Push와 Amazon EKS 배포 |
| 관측성 | Metrics Server 리소스 확인과 Fluent Bit 기반 CloudWatch 애플리케이션 로그 |

---

## AWS EKS 아키텍처

![Tanks Game Server AWS EKS 아키텍처](./docs/images/aws-eks-architecture.png)

### 주요 흐름

1. Unity 클라이언트가 NLB의 TCP 7777 포트에 접속한다.
2. NLB가 `tanks-server` Service를 통해 게임 서버 Pod로 요청을 전달한다.
3. 게임 서버는 PostgreSQL Service를 통해 플레이어와 전적 데이터를 저장한다.
4. PostgreSQL의 데이터는 EBS CSI가 생성한 암호화 gp3 볼륨에 유지된다.
5. GitHub Actions는 OIDC 임시 자격 증명으로 서버 이미지를 ECR에 Push한다.
6. Fluent Bit은 게임 서버의 표준 출력 로그를 CloudWatch Logs로 전송한다.

상세한 설계 선택, Terraform 코드와 재생성·삭제 절차는 [인프라 문서](./docs/infrastructure.md)에서 확인할 수 있다.

---

## 부하 테스트 결과

외부 인터넷에서 AWS NLB로 접속해 로그인한 뒤, 각 클라이언트가 연결을 유지하며 1초마다 방 목록을 요청하는 시나리오로 측정하였습니다.

| 동시 클라이언트 | 테스트 시간 | 성공 요청 | 실패 요청 | 처리량 | p50 | p95 | p99 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 20 | 30초 | 599 | 0 | 19.96 req/s | 12.04ms | 16.52ms | 17.97ms |
| 100 | 60초 | 5,832 | 0 | 97.17 req/s | 12.40ms | 15.39ms | 22.81ms |
| 300 | 60초 | 16,942 | 0 | 282.25 req/s | 12.20ms | 15.42ms | 19.49ms |
| **1,000** | **90초** | **78,606** | **0** | **873.17 req/s** | **12.39ms** | **16.57ms** | **83.62ms** |

### 측정 결과

- 최대 1,000개의 TCP 연결과 로그인이 모두 성공했습니다.
- 78,606건의 요청을 실패 없이 처리했습니다.
- p95 지연 시간은 16.57ms였으며, p99에서는 83.62ms의 꼬리 지연이 관찰되었습니다.
- 테스트 직후 게임 서버 Pod의 사용량은 CPU 44m, 메모리 128Mi
- 측정값은 로비 조회 중심 시나리오의 결과이므로 서버의 최대 처리 한계를 의미하지는 않습니다.

전체 조건과 제한 사항은 [부하 테스트 결과 문서](./docs/load-test-results.md)에 기록했습니다.

---

## 기술 스택

| 영역 | 기술 |
|---|---|
| Client | Unity, C# |
| Server | .NET 10, C#, 비동기 TCP |
| Protocol | JSON Lines |
| Database | PostgreSQL 18, Npgsql |
| Container | Docker |
| Infrastructure as Code | Terraform, AWS Provider |
| Kubernetes | Amazon EKS, Kustomize, Helm |
| AWS | VPC, ECR, EKS, IAM, EBS, NLB, CloudWatch Logs |
| CI/CD | GitHub Actions, GitHub OIDC |
| Observability | Metrics Server, Fluent Bit |

---

## 저장소 구성

| 경로 | 역할 |
|---|---|
| [`Assets/PortfolioTanks`](./Assets/PortfolioTanks) | Unity 네트워크 클라이언트, UI와 멀티플레이어 전투 코드 |
| [`Server/Tanks.Server`](./Server/Tanks.Server) | .NET TCP 게임 서버 |
| [`LoadTest/Tanks.LoadTest`](./LoadTest/Tanks.LoadTest) | TCP 동시 접속 부하 테스트 클라이언트 |
| [`db/migrations`](./db/migrations) | PostgreSQL 스키마 마이그레이션 |
| [`infra`](./infra) | AWS 리소스를 선언한 Terraform 코드 |
| [`k8s`](./k8s) | PostgreSQL, 게임 서버와 로그 수집 Kubernetes 매니페스트 |
| [`.github/workflows`](./.github/workflows) | ECR 이미지 빌드·Push 자동화 |
| [`docs`](./docs) | 서버, 인프라와 성능 검증 상세 문서 |

---

## 설계에서 중점적으로 다룬 부분

### 서버

- TCP는 메시지 경계가 없으므로 줄바꿈을 기준으로 JSON 메시지를 프레이밍
- 하나의 세션에서 수신과 송신을 분리하고 송신 큐를 직렬화해 패킷 섞임을 방지
- 방 상태 변경은 잠금 범위를 제한하고, 실제 네트워크 송신은 잠금 밖에서 수행
- 클라이언트가 보낸 전투 이벤트를 검증한 후 동일한 방의 참가자에게 중계
- 서버 재시작과 클라이언트 재연결 이후에도 전적이 유지되도록 PostgreSQL을 사용

### 인프라

- AWS 리소스를 Terraform으로 선언해 환경을 삭제한 뒤 동일하게 재생성
- GitHub Actions는 장기 Access Key 대신 OIDC 임시 자격 증명을 사용
- EKS 워크로드의 AWS 권한은 Pod Identity를 통해 ServiceAccount 단위로 분리
- 데이터베이스에는 EBS CSI 기반 암호화 gp3 영속 볼륨을 연결
- 게임 서버 로그만 Fluent Bit으로 수집하고 CloudWatch 보존 기간을 7일로 제한

---

## 현재 한계와 다음 개선

- 게임 서버와 PostgreSQL이 각각 단일 Pod이므로 노드 또는 가용 영역 장애에 취약 -> 워커노드를 2개이상으로 늘려 다른 az에 배치해
- 전투 상태가 서버 메모리에 있어 게임 서버를 여러 Replica로 즉시 확장 불가 -> 로비 및 세션정보는 redis에 저장하여 pod간의 이벤트는 redis pub이나 sub으로 전달
- NLB와 게임 서버 사이가 TCP 평문이므로 TLS 적용이 필요 -> route 53 도메인과 acm인증서를 준비해야함
- PostgreSQL 백업과 복구 자동화가 아직 없음 -> RDS 자동 백업과 점 복구 기능을 사용 
- 현재 부하 테스트는 로비 조회 중심이며 전투 패킷 방송 시나리오를 추가 필요 -> 다양한 부하 생성 시나리오를 설계
- 배포 이미지 SHA를 Kubernetes 매니페스트에 반영하는 과정은 아직 수동 ->GitHub Actions에서 새 이미지 SHA로 매니페스트를 자동 변경

구현 상세와 개선 방향은 [서버 문서](./docs/server.md)와 [인프라 문서](./docs/infrastructure.md)에 구분해 정리했다.
