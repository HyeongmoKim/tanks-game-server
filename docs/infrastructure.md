# Tanks Game Server Infrastructure

Unity TCP 게임 서버를 AWS에 반복 배포하고 검증하기 위해 Terraform과 Kubernetes로 구성한 개발 환경이다.

AWS 리소스는 Terraform으로 관리하고, 애플리케이션과 PostgreSQL은 Kubernetes 매니페스트와 Kustomize로 배포한다. GitHub Actions는 서버 이미지를 빌드해 Amazon ECR에 저장하며, 외부 클라이언트는 Network Load Balancer를 통해 게임 서버에 접속한다.

## 기술 스택

| 영역 | 기술 |
|---|---|
| Infrastructure as Code | Terraform 1.15, AWS Provider 6.x |
| Cloud | AWS Seoul Region |
| Container Registry | Amazon ECR |
| Kubernetes | Amazon EKS 1.36 |
| Compute | EKS Managed Node Group, Amazon Linux 2023 |
| Load Balancing | AWS Load Balancer Controller, NLB |
| Persistent Storage | Amazon EBS CSI, encrypted gp3 |
| Database | PostgreSQL 18 StatefulSet |
| CI/CD | GitHub Actions, GitHub OIDC |
| Metrics | Metrics Server |
| Logging | Fluent Bit, CloudWatch Logs |

## 문서 구성

1. 설계 목표와 범위
2. 전체 아키텍처
3. Terraform 구성
4. 네트워크
5. EKS 클러스터와 워커 노드
6. IAM과 Workload Identity
7. ECR과 GitHub Actions CI/CD
8. Kubernetes 애플리케이션 배포
9. PostgreSQL 영속 스토리지
10. 외부 TCP 트래픽
11. 모니터링과 로그
12. 배포, 재생성과 삭제
13. 보안 설계
14. 비용과 운영상 주의점
15. 현재 한계와 개선 방향

---

## 1. 설계 목표와 범위

### 목표

- 로컬에서 검증한 .NET TCP 서버를 실제 AWS Kubernetes 환경에 배포
- AWS 콘솔에서 수동 생성하는 대신 코드로 동일한 환경을 다시 만들 수 있음
- 서버 이미지를 개인 컴퓨터에서 직접 올리지 않고 GitHub Actions에서 빌드
- TCP 7777 서비스를 인터넷에 공개하고 실제 외부 부하 테스트를 수행
- PostgreSQL 데이터에 영속 볼륨을 연결
- Kubernetes 메트릭과 게임 서버 로그를 확인할 수 있음
- 사용하지 않을 때 인프라를 삭제해 개발 비용을 통제

### 환경 성격

 운영 환경 수준의 다중 AZ 고가용성, 자동 확장, 데이터베이스 백업, TLS와 재해 복구까지 구현한 구조는 아닙니다.

---

## 2. 전체 아키텍처

![Tanks Game Server AWS EKS 아키텍처](./images/aws-eks-architecture.png)

### 주요 흐름

| 흐름 | 경로 |
|---|---|
| 게임 트래픽 | Unity/부하 테스트 → NLB → Kubernetes Service → 게임 서버 Pod |
| 데이터 저장 | 게임 서버 → PostgreSQL Service → StatefulSet → EBS gp3 |
| 이미지 배포 | Git push → GitHub Actions → OIDC IAM Role → ECR |
| 이미지 실행 | EKS 워커 노드 → ECR 이미지 Pull → 게임 서버 Pod |
| 애플리케이션 로그 | 게임 서버 stdout → Fluent Bit → CloudWatch Logs |
| 제어 평면 로그 | EKS Control Plane → CloudWatch Logs |
| AWS 리소스 관리 | 개발자 PC → Terraform → AWS API |
| Kubernetes 관리 | 개발자 PC → kubectl/Helm → EKS API |

다이어그램의 편집 가능한 벡터 원본은 [`aws-eks-architecture.svg`](./images/aws-eks-architecture.svg)

---

## 3. Terraform 구성

### Infrastructure as Code

Terraform 파일이 AWS 인프라의 목표 상태를 선언한다. `plan`으로 변경 내용을 검토하고 `apply`로 생성하였습니다.

<details>
<summary><strong>Terraform과 AWS Provider 설정 보기</strong></summary>

[`providers.tf`](../infra/providers.tf)의 내용이다.

```hcl
terraform {
  required_version = ">= 1.15.0, < 2.0.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 6.0"
    }
  }
}

provider "aws" {
  profile = "tanks-terraform"
  region  = "ap-northeast-2"

  default_tags {
    tags = {
      Project     = "tanks"
      Environment = "dev"
      ManagedBy   = "Terraform"
    }
  }
}
```

#### 코드 설명

- Terraform과 AWS Provider의 호환 버전 범위를 고정
- 모든 AWS 리소스를 서울 리전에 생성
- 공통 태그로 프로젝트, 환경과 관리 주체
- 인증 정보 자체는 Terraform 코드에 저장하지 않고 AWS CLI 프로필에서 가져옴

</details>

### 파일 분리

| 파일 | 역할 |
|---|---|
| [`providers.tf`](../infra/providers.tf) | Terraform과 AWS Provider 버전, 리전, 공통 태그 |
| [`network.tf`](../infra/network.tf) | VPC, 공개 서브넷, Internet Gateway와 라우팅 |
| [`eks-iam.tf`](../infra/eks-iam.tf) | EKS Control Plane과 워커 노드 IAM 역할 |
| [`eks-cluster.tf`](../infra/eks-cluster.tf) | EKS 클러스터와 제어 평면 로그 |
| [`eks-nodes.tf`](../infra/eks-nodes.tf) | EKS 관리형 노드 그룹 |
| [`eks-storage.tf`](../infra/eks-storage.tf) | Pod Identity Agent와 EBS CSI add-on |
| [`eks-load-balancer.tf`](../infra/eks-load-balancer.tf) | AWS Load Balancer Controller 권한 |
| [`eks-observability.tf`](../infra/eks-observability.tf) | Metrics Server add-on |
| [`eks-logging.tf`](../infra/eks-logging.tf) | Fluent Bit IAM 권한과 애플리케이션 로그 그룹 |
| [`ecr.tf`](../infra/ecr.tf) | 서버 이미지 저장소 |
| [`github-actions.tf`](../infra/github-actions.tf) | GitHub OIDC Provider와 ECR Push 역할 |

Terraform은 파일 이름 순서가 아니라 리소스 참조와 `depends_on`을 이용해 생성 순서를 계산한다.

---

## 4. 네트워크

### VPC와 공개 서브넷

하나의 VPC 안에 서로 다른 가용 영역의 공개 서브넷 두 개를 만듬. NLB가 여러 가용 영역에 생성될 수 있도록 두 서브넷 모두 Kubernetes ELB 태그.

<details>
<summary><strong>VPC와 서브넷 Terraform 코드 보기</strong></summary>

[`network.tf`](../infra/network.tf)의 핵심 부분

```hcl
resource "aws_vpc" "main" {
  cidr_block           = "10.20.0.0/16"
  enable_dns_support   = true
  enable_dns_hostnames = true
}

resource "aws_subnet" "public" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = "10.20.1.0/24"
  availability_zone       = data.aws_availability_zones.available.names[0]
  map_public_ip_on_launch = true

  tags = {
    "kubernetes.io/role/elb" = "1"
  }
}

resource "aws_subnet" "public_secondary" {
  vpc_id                  = aws_vpc.main.id
  cidr_block              = "10.20.2.0/24"
  availability_zone       = data.aws_availability_zones.available.names[1]
  map_public_ip_on_launch = true

  tags = {
    "kubernetes.io/role/elb" = "1"
  }
}
```

#### 코드 설명

- `/16` VPC 안에서 각 공개 서브넷에 `/24` 주소 범위를 할당
- `available.names[0]`과 `[1]`로 서로 다른 가용 영역을 선택
- `kubernetes.io/role/elb=1` 태그로 공개 Load Balancer를 배치할 서브넷임을 표시

</details>

### 인터넷 라우팅

<details>
<summary><strong>Internet Gateway와 기본 경로 보기</strong></summary>

```hcl
resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id
}

resource "aws_route" "internet" {
  route_table_id         = aws_route_table.public.id
  destination_cidr_block = "0.0.0.0/0"
  gateway_id             = aws_internet_gateway.main.id
}
```

`0.0.0.0/0` 목적지 트래픽을 Internet Gateway로 보내므로 두 서브넷은 공개 서브넷으로 동작

</details>

### 선택 이유와 한계

개발 환경에서 NAT Gateway 비용과 구성을 줄이기 위해 워커 노드도 공개 서브넷에 배치했스비다. 운영 환경이라면 워커 노드는 사설 서브넷에 두고 NLB만 공개 서브넷에 배치하며, 필요한 외부 통신은 NAT Gateway 또는 VPC Endpoint로 제한하는 구성이 더 적합합니다.

---

## 5. EKS 클러스터와 워커 노드

### EKS Control Plane

<details>
<summary><strong>EKS 클러스터 Terraform 코드 보기</strong></summary>

[`eks-cluster.tf`](../infra/eks-cluster.tf)의 핵심 부분

```hcl
resource "aws_eks_cluster" "main" {
  name     = "tanks-dev"
  role_arn = aws_iam_role.eks_cluster.arn
  version  = "1.36"

  enabled_cluster_log_types = [
    "api",
    "audit",
    "authenticator",
    "controllerManager",
    "scheduler"
  ]

  access_config {
    authentication_mode = "API_AND_CONFIG_MAP"
  }

  vpc_config {
    subnet_ids = [
      aws_subnet.public.id,
      aws_subnet.public_secondary.id
    ]

    endpoint_private_access = true
    endpoint_public_access  = true
    public_access_cidrs     = ["0.0.0.0/0"]
  }
}
```

#### 코드 설명

- EKS가 관리하는 Kubernetes Control Plane을 두 서브넷에 연결
- API, 인증, 감사, 스케줄러와 컨트롤러 로그를 CloudWatch로 전송
- API와 ConfigMap 인증 모드를 함께 사용
- 로컬 PC의 `kubectl` 접근을 위해 공개 API Endpoint를 활성화

</details>

현재 공개 API 허용 범위가 `0.0.0.0/0`이므로 개발 편의성은 높지만 공격 표면도 넓음. 운영 환경에서는 관리용 고정 IP 또는 VPN 대역으로 `public_access_cidrs`를 제한해야함.

### 관리형 노드 그룹

<details>
<summary><strong>EKS Managed Node Group 코드 보기</strong></summary>

[`eks-nodes.tf`](../infra/eks-nodes.tf)의 핵심 부분

```hcl
resource "aws_eks_node_group" "main" {
  cluster_name    = aws_eks_cluster.main.name
  node_group_name = "tanks-dev-general"
  node_role_arn   = aws_iam_role.eks_nodes.arn

  subnet_ids = [
    aws_subnet.public.id,
    aws_subnet.public_secondary.id
  ]

  ami_type      = "AL2023_x86_64_STANDARD"
  capacity_type = "ON_DEMAND"
  instance_types = [
    "c7i-flex.large"
  ]

  disk_size = 30

  scaling_config {
    desired_size = 1
    min_size     = 1
    max_size     = 2
  }
}
```

#### 코드 설명

- Amazon Linux 2023 x86_64 노드를 사용
- 현재 원하는 노드 수는 1대이고 설정 가능한 상한은 2대
- On-Demand 인스턴스를 사용해 Spot 중단 변수를 제외
- 두 서브넷을 전달해 노드 그룹이 두 가용 영역을 사용할 수 있음

</details>

`max_size=2`만으로 CPU 사용량에 따라 자동 확장되지는 않음. 현재 Cluster Autoscaler나 Karpenter가 없으므로 노드 수는 Terraform 또는 AWS 설정으로 직접 변경해야 함.

### EKS add-on

| Add-on | 역할 |
|---|---|
| VPC CNI | Pod 네트워크와 VPC IP 연결 |
| CoreDNS | 클러스터 내부 DNS |
| kube-proxy | Kubernetes Service 네트워크 규칙 |
| EKS Pod Identity Agent | 서비스 계정에 연결된 임시 AWS 자격 증명 제공 |
| EBS CSI Driver | PVC 요청에 맞춰 EBS 볼륨 생성과 연결 |
| Metrics Server | `kubectl top`과 리소스 메트릭 API 제공 |

---

## 6. IAM과 Workload Identity

### 역할 분리

AWS API 권한을 하나의 관리자 역할에 몰아주지 않고 사용 주체별로 분리

| IAM 역할 | 사용 주체 | 주요 권한 |
|---|---|---|
| EKS Cluster Role | EKS Control Plane | 클러스터 관리 |
| EKS Node Role | EC2 워커 노드 | 노드 등록, ECR Pull, VPC CNI, SSM |
| Load Balancer Controller Role | Kubernetes ServiceAccount | NLB 생성과 관리 |
| EBS CSI Role | EBS CSI Controller ServiceAccount | 클러스터 범위 EBS 관리 |
| Fluent Bit Role | Fluent Bit ServiceAccount | 지정 CloudWatch Log Group 쓰기 |
| GitHub Actions Role | GitHub OIDC 세션 | 지정 ECR 저장소 Push |

### EKS Pod Identity

AWS 권한이 필요한 Kubernetes 구성요소에는 노드 역할 전체를 공유하는 대신 ServiceAccount별 IAM 역할을 연결

<details>
<summary><strong>Pod Identity 신뢰 정책과 연결 코드 보기</strong></summary>

[`eks-logging.tf`](../infra/eks-logging.tf)의 Fluent Bit 예시

```hcl
data "aws_iam_policy_document" "fluent_bit_trust" {
  statement {
    actions = [
      "sts:AssumeRole",
      "sts:TagSession"
    ]

    principals {
      type        = "Service"
      identifiers = ["pods.eks.amazonaws.com"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:RequestTag/kubernetes-namespace"
      values   = ["amazon-cloudwatch"]
    }

    condition {
      test     = "StringEquals"
      variable = "aws:RequestTag/kubernetes-service-account"
      values   = ["fluent-bit"]
    }
  }
}

resource "aws_eks_pod_identity_association" "fluent_bit" {
  cluster_name    = aws_eks_cluster.main.name
  namespace       = "amazon-cloudwatch"
  service_account = "fluent-bit"
  role_arn        = aws_iam_role.fluent_bit.arn
}
```

#### 코드 설명

- 역할을 사용할 수 있는 Kubernetes Namespace와 ServiceAccount를 제한.
- 정적 Access Key를 Kubernetes Secret에 저장하지 않음.
- Pod Identity Agent가 Pod에 임시 자격 증명을 제공.
- Load Balancer Controller와 EBS CSI에도 같은 패턴을 사용.

</details>

### Fluent Bit 최소 권한

<details>
<summary><strong>CloudWatch Logs 권한 코드 보기</strong></summary>

```hcl
data "aws_iam_policy_document" "fluent_bit_logs" {
  statement {
    actions = [
      "logs:CreateLogStream",
      "logs:DescribeLogStreams",
      "logs:PutLogEvents"
    ]

    resources = [
      "${aws_cloudwatch_log_group.tanks_application.arn}:*"
    ]
  }
}
```

Fluent Bit은 지정한 애플리케이션 로그 그룹의 스트림 생성과 이벤트 기록만 수행할 수 있음. 전체 CloudWatch 권한이나 노드 IAM 역할을 사용하지 않음.

</details>

---

## 7. ECR과 GitHub Actions CI/CD

### ECR 저장소

<details>
<summary><strong>ECR Terraform 코드 보기</strong></summary>

[`ecr.tf`](../infra/ecr.tf)의 내용

```hcl
resource "aws_ecr_repository" "server" {
  name                 = "tanks-server-dev"
  image_tag_mutability = "IMMUTABLE"
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }
}
```

#### 코드 설명

- 같은 태그를 다른 이미지로 덮어쓰지 못하게 함.
- Push 시 이미지 스캔을 실행.
- Git commit SHA를 이미지 태그로 사용해 실행 중인 코드 버전을 추적할 수 있음.
- `force_delete=true`이므로 Terraform 전체 삭제 시 저장된 이미지도 함께 삭제.

</details>

### GitHub OIDC 인증

GitHub Actions에 장기 AWS Access Key를 저장하지 않음. Workflow 실행 시 GitHub가 발급한 OIDC 토큰으로 제한된 IAM Role을 임시로 사용.

<details>
<summary><strong>GitHub OIDC 신뢰 조건 보기</strong></summary>

아래 코드는 저장소 식별자를 일반화한 핵심 구조. 실제 값은 [`github-actions.tf`](../infra/github-actions.tf)에 정의.

```hcl
condition {
  test     = "StringEquals"
  variable = "token.actions.githubusercontent.com:aud"
  values   = ["sts.amazonaws.com"]
}

condition {
  test     = "StringEquals"
  variable = "token.actions.githubusercontent.com:sub"
  values = [
    "repo:<OWNER>/<REPOSITORY>:ref:refs/heads/main"
  ]
}
```

Audience를 AWS STS로 제한하고 Subject 조건으로 지정 저장소의 `main` 브랜치만 역할을 사용할 수 있게 함.

</details>

### ECR Push 최소 권한

<details>
<summary><strong>GitHub Actions ECR 권한 보기</strong></summary>

```hcl
statement {
  actions   = ["ecr:GetAuthorizationToken"]
  resources = ["*"]
}

statement {
  actions = [
    "ecr:BatchCheckLayerAvailability",
    "ecr:CompleteLayerUpload",
    "ecr:InitiateLayerUpload",
    "ecr:PutImage",
    "ecr:UploadLayerPart"
  ]

  resources = [aws_ecr_repository.server.arn]
}
```

인증 토큰 조회 외의 이미지 Push 권한은 이 프로젝트의 ECR 저장소 ARN으로 제한.

</details>

### 이미지 빌드 Workflow

<details>
<summary><strong>GitHub Actions Workflow 보기</strong></summary>

[`build-server-image.yml`](../.github/workflows/build-server-image.yml)의 핵심 부분.

```yaml
permissions:
  contents: read
  id-token: write

steps:
  - uses: actions/checkout@v6

  - uses: aws-actions/configure-aws-credentials@v6.2.3
    with:
      role-to-assume: ${{ vars.AWS_ROLE_ARN }}
      aws-region: ${{ env.AWS_REGION }}

  - id: login-ecr
    uses: aws-actions/amazon-ecr-login@v2

  - uses: docker/build-push-action@v7
    with:
      context: ./Server/Tanks.Server
      push: true
      tags: ${{ steps.login-ecr.outputs.registry }}/${{ env.ECR_REPOSITORY }}:${{ github.sha }}
```

#### 코드 설명

- 서버 폴더 또는 Workflow가 변경될 때만 자동 실행
- `id-token: write`는 OIDC 토큰 발급에 사용
- Docker Buildx로 이미지를 빌드하고 commit SHA 태그로 Push
- AWS 계정 번호, 역할 ARN과 저장소 이름은 GitHub Repository Variable로 전달

</details>

### 현재 배포 방식의 범위

Workflow는 이미지 빌드와 ECR Push까지 담당. Kubernetes 매니페스트의 이미지 SHA 변경과 `kubectl apply`는 현재 수동 단계다. 완전한 CD로 확장하려면 이미지 태그 업데이트 PR, Argo CD 또는 Flux 같은 GitOps 흐름을 추가할 수 있음.

---

## 8. Kubernetes 애플리케이션 배포

### Kustomize 구성

애플리케이션 리소스는 루트 [`kustomization.yaml`](../kustomization.yaml)에서 조합한다.

<details>
<summary><strong>Kustomize 구성 보기</strong></summary>

```yaml
namespace: tanks

resources:
  - k8s/base/00-foundation.yaml
  - k8s/base/10-postgres.yaml
  - k8s/base/20-server.yaml

configMapGenerator:
  - name: postgres-init
    namespace: tanks
    files:
      - 001_create_players.sql=db/migrations/001_create_players.sql

generatorOptions:
  disableNameSuffixHash: true
```

#### 코드 설명

- Namespace, StorageClass, PostgreSQL과 게임 서버를 한 명령으로 적용.
- SQL 마이그레이션 파일을 PostgreSQL 초기화용 ConfigMap으로 만듬.
- ConfigMap 이름을 고정해 StatefulSet의 `subPath` 참조를 유지.

</details>

### Namespace 보안 수준

<details>
<summary><strong>Pod Security 라벨 보기</strong></summary>

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: tanks
  labels:
    pod-security.kubernetes.io/enforce: baseline
    pod-security.kubernetes.io/audit: restricted
    pod-security.kubernetes.io/warn: restricted
```

`baseline` 위반은 차단하고 더 엄격한 `restricted` 기준 위반은 감사와 경고로 확인.

</details>

### 게임 서버 Deployment

| 항목 | 설정 |
|---|---|
| Replica | 1 |
| 전략 | Recreate |
| CPU Request / Limit | 250m / 1 core |
| Memory Request / Limit | 256Mi / 768Mi |
| 상태 검사 | TCP startup, readiness, liveness |
| 파일 시스템 | 읽기 전용 Root Filesystem, `/tmp`만 `emptyDir` |
| 권한 | 비루트, 모든 Linux Capability 제거 |
| DB 연결 | Kubernetes Secret 환경 변수 |

<details>
<summary><strong>게임 서버 보안 컨텍스트 보기</strong></summary>

[`20-server.yaml`](../k8s/base/20-server.yaml)의 핵심 부분.

```yaml
automountServiceAccountToken: false

securityContext:
  runAsNonRoot: true
  seccompProfile:
    type: RuntimeDefault

containers:
  - name: tanks-server
    securityContext:
      allowPrivilegeEscalation: false
      readOnlyRootFilesystem: true
      capabilities:
        drop:
          - ALL
```

게임 서버는 Kubernetes API를 호출하지 않으므로 ServiceAccount 토큰을 자동 마운트하지 않음. 컨테이너 권한 상승, Root 실행과 Root Filesystem 변경도 제한.

</details>

### Secret을 Git과 분리

DB 비밀번호와 연결 문자열은 저장소에 YAML로 커밋하지 않고 클러스터에 별도로 생성한. Secret을 Terraform에 직접 넣지 않은 이유는 로컬 Terraform State에 평문 값이 남는 범위를 줄이기 위함.

현재는 수동 생성 방식이며, 운영 환경에서는 AWS Secrets Manager와 External Secrets Operator 같은 자동화된 비밀 관리 방식을 검토할 수 있음.

---

## 9. PostgreSQL 영속 스토리지

### StatefulSet 선택

PostgreSQL은 고정된 Pod 이름과 PVC 연결이 필요하므로 Deployment가 아니라 StatefulSet을 사용.

| 구성 | 역할 |
|---|---|
| `postgres` ClusterIP Service | 게임 서버의 DB 접속 주소 제공 |
| `postgres-headless` Service | StatefulSet의 안정적인 네트워크 식별자 |
| StatefulSet | PostgreSQL Pod와 PVC 수명주기 관리 |
| ConfigMap | 최초 DB 생성 시 실행할 SQL 제공 |
| Secret | DB명, 사용자, 비밀번호 제공 |
| PVC | PostgreSQL 데이터 디렉터리 영속화 |

### 암호화된 gp3 StorageClass

<details>
<summary><strong>StorageClass 코드 보기</strong></summary>

[`00-foundation.yaml`](../k8s/base/00-foundation.yaml)의 핵심 부분.

```yaml
apiVersion: storage.k8s.io/v1
kind: StorageClass
metadata:
  name: gp3-encrypted
provisioner: ebs.csi.aws.com
parameters:
  type: gp3
  encrypted: "true"
reclaimPolicy: Delete
volumeBindingMode: WaitForFirstConsumer
allowVolumeExpansion: true
```

#### 코드 설명

- EBS CSI Driver가 암호화된 gp3 볼륨을 동적으로 만듬.
- `WaitForFirstConsumer`로 Pod가 배치될 가용 영역이 정해진 뒤 볼륨을 생성.
- PVC 용량 확장을 허용.
- `reclaimPolicy: Delete`이므로 PVC 삭제 시 EBS 데이터도 삭제될 수 있음.

</details>

### PostgreSQL PVC

<details>
<summary><strong>StatefulSet 볼륨 요청 보기</strong></summary>

```yaml
volumeClaimTemplates:
  - metadata:
      name: postgres-data
    spec:
      accessModes:
        - ReadWriteOncePod
      storageClassName: gp3-encrypted
      resources:
        requests:
          storage: 5Gi
```

한 PostgreSQL Pod만 볼륨을 읽고 쓰도록 `ReadWriteOncePod`를 사용. 현재 DB는 단일 인스턴스이므로 가용 영역 장애에 대한 자동 Failover는 제공하지 않음.

</details>

### EBS CSI 권한

EBS CSI Controller에는 EKS Pod Identity와 클러스터 범위 AWS 관리형 정책을 연결. 이를 통해 다른 클러스터의 볼륨까지 관리할 수 있는 범위를 줄임.

---

## 10. 외부 TCP 트래픽

### NLB 선택 이유

게임 서버는 HTTP가 아니라 지속적인 TCP 연결을 사용. Layer 7 HTTP 라우팅을 위한 ALB 대신 Layer 4의 Network Load Balancer를 선택.

### LoadBalancer Service

<details>
<summary><strong>NLB Service 매니페스트 보기</strong></summary>

[`20-server.yaml`](../k8s/base/20-server.yaml)의 Service 부분.

```yaml
apiVersion: v1
kind: Service
metadata:
  name: tanks-server
  namespace: tanks
  annotations:
    service.beta.kubernetes.io/aws-load-balancer-scheme: internet-facing
    service.beta.kubernetes.io/aws-load-balancer-nlb-target-type: ip
    service.beta.kubernetes.io/aws-load-balancer-healthcheck-protocol: TCP
spec:
  type: LoadBalancer
  loadBalancerClass: service.k8s.aws/nlb

  selector:
    app.kubernetes.io/name: tanks-server

  ports:
    - name: game
      port: 7777
      targetPort: game
      protocol: TCP
```

#### 코드 설명

- `internet-facing`으로 외부에서 접근 가능한 NLB를 생성.
- `ip` Target Type을 사용해 EC2 NodePort가 아니라 게임 서버 Pod IP를 Target으로 등록.
- TCP 7777 상태 검사와 트래픽 전달을 사용.
- Service Selector가 `tanks-server` Pod를 찾음.

</details>

### AWS Load Balancer Controller

Controller는 Kubernetes `Service`를 감시하고 필요한 NLB, Target Group과 네트워크 리소스를 AWS에 생성.

Controller 자체는 Helm으로 설치하고, IAM Role과 Pod Identity 연결은 Terraform으로 관리. IAM 정책 파일과 Helm Chart 버전은 `3.5.0`으로 맞춤.

<details>
<summary><strong>Load Balancer Controller Pod Identity 보기</strong></summary>

[`eks-load-balancer.tf`](../infra/eks-load-balancer.tf)의 핵심 부분.

```hcl
resource "aws_eks_pod_identity_association"
    "load_balancer_controller" {
  cluster_name = aws_eks_cluster.main.name
  namespace    = "kube-system"

  service_account =
    "aws-load-balancer-controller"

  role_arn =
    aws_iam_role.load_balancer_controller.arn
}
```

</details>

### 실제 트래픽 경로

<details>
<summary><strong>외부 클라이언트부터 DB까지의 경로 보기</strong></summary>

```text
Unity Client
  → NLB DNS:7777
  → NLB Target Group
  → tanks-server Service
  → tanks-server Pod:7777
  → postgres Service:5432
  → postgres-0 Pod
```

</details>

---

## 11. 모니터링과 로그

### Metrics Server

Metrics Server EKS add-on을 사용해 Pod와 Node의 현재 CPU·메모리 사용량을 확인.

<details>
<summary><strong>Metrics Server Terraform 코드 보기</strong></summary>

[`eks-observability.tf`](../infra/eks-observability.tf)의 내용.

```hcl
data "aws_eks_addon_version" "metrics_server" {
  addon_name         = "metrics-server"
  kubernetes_version = aws_eks_cluster.main.version
  most_recent        = true
}

resource "aws_eks_addon" "metrics_server" {
  cluster_name  = aws_eks_cluster.main.name
  addon_name    = "metrics-server"
  addon_version = data.aws_eks_addon_version.metrics_server.version
}
```

확인 명령은 다음과 같습니다.

```powershell
kubectl top pods -n tanks
kubectl top nodes
```

Metrics Server 값은 장기 시계열이나 테스트 중 최대값이 아니라 조회 시점의 현재 사용량.

</details>

### EKS Control Plane 로그

Terraform에서 로그 그룹을 먼저 만들고 보존 기간을 7일로 설정.

<details>
<summary><strong>Control Plane 로그 그룹 보기</strong></summary>

```hcl
resource "aws_cloudwatch_log_group" "eks_cluster" {
  name              = "/aws/eks/tanks-dev/cluster"
  retention_in_days = 7
}

enabled_cluster_log_types = [
  "api",
  "audit",
  "authenticator",
  "controllerManager",
  "scheduler"
]
```

이 로그는 Kubernetes API와 Control Plane 동작 기록이며 게임 서버의 `Console.WriteLine()` 로그와는 다름.

</details>

### 게임 서버 애플리케이션 로그

전체 Container Insights와 Application Signals를 활성화하지 않고 Fluent Bit DaemonSet이 게임 서버 컨테이너 로그만 CloudWatch로 보냄. 불필요한 메트릭과 다른 시스템 Pod 로그 수집을 피하기 위한 비용 중심 선택.

<details>
<summary><strong>Fluent Bit 로그 선택과 출력 설정 보기</strong></summary>

[`10-config.yaml`](../k8s/observability/10-config.yaml)의 핵심 부분.

```ini
[INPUT]
    Name  tail
    Tag   kube.*
    Path  /var/log/containers/tanks-server-*_tanks_tanks-server-*.log
    multiline.parser  docker, cri
    Read_from_Head    Off

[OUTPUT]
    Name               cloudwatch_logs
    Match              kube.*
    region             ap-northeast-2
    log_group_name     /aws/containerinsights/tanks-dev/application
    log_stream_prefix  tanks-
    auto_create_group  false
    Retry_Limit        10
```

#### 코드 설명

- 파일 경로 패턴으로 `tanks` Namespace의 `tanks-server` 컨테이너만 선택.
- 설치 전의 전체 파일이 아니라 설치 후 추가되는 로그부터 읽음.
- Log Group은 Terraform이 만들고 Fluent Bit은 Log Stream만 생성.
- 로그와 Kubernetes 메타데이터를 CloudWatch Logs로 보냄.

</details>

### 로그 그룹

| Log Group | 내용 | 보존 기간 |
|---|---|---:|
| `/aws/eks/tanks-dev/cluster` | EKS Control Plane 로그 | 7일 |
| `/aws/containerinsights/tanks-dev/application` | 게임 서버 애플리케이션 로그 | 7일 |

로컬에서 즉시 확인할 때는 다음 명령도 사용할 수 있다.

<details>
<summary><strong>Kubernetes 로그 확인 명령 보기</strong></summary>

```powershell
kubectl logs -n tanks deployment/tanks-server
kubectl logs -n tanks postgres-0
kubectl logs -n amazon-cloudwatch daemonset/fluent-bit
```

</details>

---

## 12. 배포, 재생성과 삭제

### 사전 요구사항

- AWS CLI 인증 프로필
- Terraform
- kubectl
- Helm
- Docker 또는 GitHub Actions
- GitHub Repository Variables: AWS Region, ECR 저장소 이름, IAM Role ARN

비밀번호, Access Key, NLB 주소와 AWS 계정 식별자는 저장소 문서에 기록하지 않음.

### 1단계: AWS 인프라 생성

<details>
<summary><strong>Terraform 생성 명령 보기</strong></summary>

프로젝트 루트에서 실행한다.

```powershell
terraform -chdir=.\infra init
terraform -chdir=.\infra fmt -check
terraform -chdir=.\infra validate
terraform -chdir=.\infra plan
terraform -chdir=.\infra apply
```

`plan`에서 생성·변경·삭제 대상을 검토한 뒤 `apply`를 승인.

</details>

### 2단계: kubectl 연결

<details>
<summary><strong>kubeconfig 갱신 명령 보기</strong></summary>

```powershell
aws eks update-kubeconfig `
  --name tanks-dev `
  --region ap-northeast-2 `
  --profile tanks-dev

kubectl get nodes
```

`update-kubeconfig`는 EKS API Endpoint와 인증 명령을 로컬 kubeconfig에 기록. 클러스터나 Pod를 새로 생성하는 명령은 아님.

</details>

### 3단계: AWS Load Balancer Controller 설치

<details>
<summary><strong>Helm 설치 명령 보기</strong></summary>

```powershell
helm repo add eks https://aws.github.io/eks-charts
helm repo update

$VpcId = aws eks describe-cluster `
  --name tanks-dev `
  --region ap-northeast-2 `
  --profile tanks-dev `
  --query "cluster.resourcesVpcConfig.vpcId" `
  --output text

helm upgrade --install aws-load-balancer-controller `
  eks/aws-load-balancer-controller `
  --version 3.5.0 `
  --namespace kube-system `
  --set clusterName=tanks-dev `
  --set serviceAccount.name=aws-load-balancer-controller `
  --set region=ap-northeast-2 `
  --set vpcId=$VpcId
```

Terraform이 미리 만든 Pod Identity Association이 Helm이 생성한 ServiceAccount에 IAM 역할을 연결.

</details>

### 4단계: 이미지 준비

서버 코드가 `main` 브랜치에 Push되면 GitHub Actions가 이미지를 ECR에 저장. 생성된 commit SHA 태그를 [`20-server.yaml`](../k8s/base/20-server.yaml)의 `image`에 반영.

<details>
<summary><strong>ECR 이미지 참조 형식 보기</strong></summary>

```yaml
image: <ACCOUNT_ID>.dkr.ecr.ap-northeast-2.amazonaws.com/tanks-server-dev:<GIT_SHA>
```

</details>

### 5단계: Namespace와 DB Secret 생성

<details>
<summary><strong>DB Secret 생성 명령 보기</strong></summary>

```powershell
kubectl apply -f .\k8s\base\00-foundation.yaml

$SecureDbPassword = Read-Host `
  "PostgreSQL password" `
  -AsSecureString

$DbPassword =
  [System.Net.NetworkCredential]::new(
    "",
    $SecureDbPassword).Password

kubectl create secret generic tanks-db `
  --namespace tanks `
  --from-literal="POSTGRES_DB=tanks_game" `
  --from-literal="POSTGRES_USER=tanks_app" `
  --from-literal="POSTGRES_PASSWORD=$DbPassword" `
  --from-literal="TANKS_DB_CONNECTION_STRING=Host=postgres;Port=5432;Database=tanks_game;Username=tanks_app;Password=$DbPassword;SSL Mode=Disable" `
  --dry-run=client -o yaml |
  kubectl apply -f -

Remove-Variable `
  DbPassword, SecureDbPassword `
  -ErrorAction SilentlyContinue
```

실제 비밀번호는 PowerShell 화면이나 Git 파일에 직접 작성하지 않음.

</details>

### 6단계: 워크로드와 로그 수집기 배포

<details>
<summary><strong>Kustomize 적용과 확인 명령 보기</strong></summary>

```powershell
kubectl apply -k .
kubectl apply -k .\k8s\observability

kubectl get pods,pvc,service -n tanks
kubectl get pods -n amazon-cloudwatch
```

서버가 외부 트래픽을 받을 수 있게 되면 `tanks-server` Service의 Load Balancer Hostname이 생성.

</details>

### 전체 삭제 순서

Kubernetes Service가 만든 NLB부터 제거한 뒤 Terraform 인프라를 삭제.

<details>
<summary><strong>안전한 삭제 명령 보기</strong></summary>

```powershell
kubectl delete -k .\k8s\observability
kubectl delete -k .

helm uninstall `
  aws-load-balancer-controller `
  --namespace kube-system

terraform -chdir=.\infra plan -destroy
terraform -chdir=.\infra destroy
```

`kubectl delete -k .`가 완료되고 NLB가 제거될 시간을 준 뒤 Controller와 EKS를 삭제. 그렇지 않으면 NLB 관련 AWS 리소스가 남을 수 있음.

</details>

### 삭제 시 사라지는 데이터

- ECR 저장소와 이미지: `force_delete=true`
- PostgreSQL PVC와 EBS 볼륨: `reclaimPolicy=Delete`
- CloudWatch Log Group과 보관 로그
- NLB와 EKS 클러스터

보존이 필요한 DB 데이터가 있다면 삭제 전에 Snapshot이나 논리 백업을 만들어야 함.

---

## 13. 보안 설계

### 적용한 항목

| 항목 | 적용 내용 |
|---|---|
| 장기 AWS 키 제거 | GitHub Actions에서 OIDC 임시 자격 증명 사용 |
| 워크로드 권한 분리 | EKS Pod Identity를 ServiceAccount별로 연결 |
| GitHub 신뢰 범위 | 저장소, main 브랜치와 AWS STS Audience 조건 |
| ECR | Immutable 태그, Push 시 이미지 스캔 |
| DB 비밀번호 | Git에 커밋하지 않고 Kubernetes Secret으로 생성 |
| EBS | gp3 암호화 활성화 |
| 게임 서버 컨테이너 | 비루트, 읽기 전용 Root Filesystem, Capability 제거 |
| Kubernetes API 토큰 | 필요 없는 게임 서버와 DB Pod에서 자동 마운트 비활성화 |
| Pod Security | baseline 강제, restricted 감사와 경고 |
| 로그 권한 | Fluent Bit을 지정 Log Group 쓰기로 제한 |
| 로그 보존 | 7일 후 자동 만료 |

### 남은 보안 과제

- EKS 공개 API 허용 대역 제한
- 워커 노드를 사설 서브넷으로 이동
- Unity 클라이언트와 NLB 사이의 TLS 적용
- 로그인 인증과 세션 토큰 도입
- Secret Manager 기반 비밀 회전
- Kubernetes NetworkPolicy 적용
- 이미지 스캔 결과를 CI 배포 조건으로 연결
- ECR 이미지 서명과 공급망 검증

---

## 14. 비용과 운영상 주의점

### 주요 비용 발생 지점

| 서비스 | 비용 요인 |
|---|---|
| EKS | 클러스터 Control Plane 실행 시간 |
| EC2 | 워커 노드 실행 시간과 Root EBS 30GiB |
| NLB | 실행 시간과 처리 데이터 |
| Public IPv4 | 공개 IPv4 사용 시간 |
| EBS | PostgreSQL gp3 5GiB와 Snapshot |
| CloudWatch | 로그 수집량, 저장량과 조회 |
| ECR | 컨테이너 이미지 저장량 |
| Data Transfer | 인터넷 및 가용 영역 간 전송량 |

개발 환경에서 가장 중요한 비용 제어 방법은 사용하지 않을 때 Kubernetes LoadBalancer를 먼저 삭제하고 Terraform으로 EKS, EC2와 NLB를 제거하는 것.

CloudWatch는 전체 Container Insights 대신 게임 서버 로그만 수집하고 보존 기간을 7일로 설정. 로그 출력량이 증가하면 저장 기간보다 수집량이 더 큰 비용 요인이 될 수 있으므로 불필요한 요청별 로그를 제한.

### 운영 확인 명령

<details>
<summary><strong>클러스터 상태 확인 명령 보기</strong></summary>

```powershell
kubectl get nodes
kubectl get pods -A
kubectl get service -n tanks
kubectl top pods -n tanks
kubectl logs -n tanks deployment/tanks-server
kubectl logs -n amazon-cloudwatch daemonset/fluent-bit
```

</details>

---

## 15. 현재 한계와 개선 방향

| 현재 구조 | 영향 | 개선 방향 |
|---|---|---|
| 공개 서브넷의 워커 노드 | 노드가 인터넷 경계에 가까움 | 사설 서브넷과 VPC Endpoint/NAT 구성 |
| EKS API `0.0.0.0/0` 허용 | 관리 API 공격 표면 증가 | 고정 IP, VPN 또는 사설 Endpoint로 제한 |
| 워커 노드 1대 | 노드 장애 시 서비스 중단 | 최소 2대와 가용 영역 분산 |
| 게임 서버 Replica 1 | Pod 장애·업데이트 중 서비스 공백 | 상태 외부화 후 다중 Replica와 RollingUpdate |
| `max_size=2`만 설정 | 실제 자동 확장이 일어나지 않음 | Cluster Autoscaler 또는 Karpenter 추가 |
| PostgreSQL 단일 Pod/EBS | AZ 또는 DB 장애에 취약 | Amazon RDS Multi-AZ 또는 DB 복제·백업 |
| PVC 삭제 정책 `Delete` | 전체 삭제 시 게임 데이터 소멸 | Snapshot, 백업과 환경별 Retain 정책 |
| NLB TCP 평문 | 전송 구간 암호화 없음 | TLS Listener 또는 애플리케이션 TLS |
| Terraform State 로컬 저장 | 협업과 State 유실에 취약 | 암호화된 Remote Backend와 State Locking |
| LBC Helm 설치 수동 | 완전한 원클릭 재현이 아님 | Terraform Helm Provider 또는 배포 스크립트 |
| 이미지 SHA 반영 수동 | Push 후 배포 단계가 끊어짐 | GitOps 기반 자동 이미지 업데이트 |
| HPA 없음 | Pod 자동 확장 없음 | 상태 외부화 후 HPA와 부하 기반 검증 |
| 단순 Metrics Server | 장기 추세와 최대값 확인 불가 | Prometheus/Grafana 또는 선택적 Container Insights |

## 구현을 통해 검증한 내용

- Terraform으로 VPC, IAM, ECR, EKS, Node Group, Add-on과 CloudWatch를 선언하고 재생성했다.
- Kubernetes Service를 AWS Load Balancer Controller와 연결해 외부 TCP NLB를 생성했다.
- EKS Pod Identity로 Controller와 로그 수집기의 AWS 권한을 ServiceAccount 단위로 분리했다.
- EBS CSI와 StatefulSet을 사용해 PostgreSQL에 암호화된 영속 볼륨을 연결했다.
- GitHub OIDC로 장기 Access Key 없이 서버 이미지를 ECR에 Push했다.
- Metrics Server와 Fluent Bit으로 리소스 사용량과 게임 서버 로그를 확인했다.
- 실제 외부 NLB를 통해 최대 1,000개 동시 TCP 연결의 부하 테스트를 수행했다.

## 참고 자료

- [Terraform 소개](https://developer.hashicorp.com/terraform/intro)
- [Amazon EKS Pod Identity](https://docs.aws.amazon.com/eks/latest/userguide/pod-identities.html)
- [AWS Load Balancer Controller](https://docs.aws.amazon.com/eks/latest/userguide/aws-load-balancer-controller.html)
- [EKS에서 Network Load Balancer 사용](https://docs.aws.amazon.com/eks/latest/userguide/network-load-balancing.html)
- [Amazon EBS CSI Driver](https://docs.aws.amazon.com/eks/latest/userguide/ebs-csi.html)
- [GitHub Actions에서 AWS OIDC 구성](https://docs.github.com/en/actions/how-tos/secure-your-work/security-harden-deployments/oidc-in-aws)
- [Fluent Bit으로 CloudWatch Logs 전송](https://docs.aws.amazon.com/AmazonCloudWatch/latest/monitoring/Container-Insights-setup-logs-FluentBit.html)
- [CloudWatch Log Group과 보존 기간](https://docs.aws.amazon.com/AmazonCloudWatch/latest/logs/Working-with-log-groups-and-streams.html)
