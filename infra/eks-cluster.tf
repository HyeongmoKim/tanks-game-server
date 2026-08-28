resource "aws_cloudwatch_log_group" "eks_cluster" {
  name              = "/aws/eks/tanks-dev/cluster"
  retention_in_days = 7
}

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
    authentication_mode                         = "API_AND_CONFIG_MAP"
    bootstrap_cluster_creator_admin_permissions = true
  }

  upgrade_policy {
    support_type = "STANDARD"
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

  depends_on = [
    aws_iam_role_policy_attachment.eks_cluster,
    aws_cloudwatch_log_group.eks_cluster
  ]

  tags = {
    Name = "tanks-dev"
  }
}

output "eks_cluster_name" {
  description = "EKS cluster name"
  value       = aws_eks_cluster.main.name
}

output "eks_cluster_endpoint" {
  description = "EKS Kubernetes API endpoint"
  value       = aws_eks_cluster.main.endpoint
}