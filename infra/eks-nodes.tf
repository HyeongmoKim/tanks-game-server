resource "aws_eks_node_group" "main" {
  cluster_name    = aws_eks_cluster.main.name
  node_group_name = "tanks-dev-general"
  node_role_arn   = aws_iam_role.eks_nodes.arn
  version         = aws_eks_cluster.main.version

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

  update_config {
    max_unavailable = 1
  }

  labels = {
    workload = "general"
  }

  depends_on = [
    aws_iam_role_policy_attachment.eks_nodes_worker,
    aws_iam_role_policy_attachment.eks_nodes_ecr,
    aws_iam_role_policy_attachment.eks_nodes_cni,
    aws_iam_role_policy_attachment.eks_nodes_ssm
  ]

  tags = {
    Name = "tanks-dev-general"
  }
}

output "eks_node_group_name" {
  description = "EKS managed node group name"
  value       = aws_eks_node_group.main.node_group_name
}