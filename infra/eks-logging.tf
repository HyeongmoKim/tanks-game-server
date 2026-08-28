resource "aws_cloudwatch_log_group" "tanks_application" {
  name              = "/aws/containerinsights/${aws_eks_cluster.main.name}/application"
  retention_in_days = 7
}

data "aws_iam_policy_document" "fluent_bit_trust" {
  statement {
    effect = "Allow"

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
      variable = "aws:RequestTag/eks-cluster-name"
      values   = [aws_eks_cluster.main.name]
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

resource "aws_iam_role" "fluent_bit" {
  name               = "tanks-dev-fluent-bit"
  assume_role_policy = data.aws_iam_policy_document.fluent_bit_trust.json
}

data "aws_iam_policy_document" "fluent_bit_logs" {
  statement {
    effect = "Allow"

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

resource "aws_iam_role_policy" "fluent_bit_logs" {
  name   = "cloudwatch-application-logs"
  role   = aws_iam_role.fluent_bit.id
  policy = data.aws_iam_policy_document.fluent_bit_logs.json
}

resource "aws_eks_pod_identity_association" "fluent_bit" {
  cluster_name    = aws_eks_cluster.main.name
  namespace       = "amazon-cloudwatch"
  service_account = "fluent-bit"
  role_arn        = aws_iam_role.fluent_bit.arn

  depends_on = [
    aws_eks_addon.pod_identity_agent,
    aws_iam_role_policy.fluent_bit_logs
  ]
}