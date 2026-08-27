resource "aws_ecr_repository" "server" {
  name                 = "tanks-server-dev"
  image_tag_mutability = "IMMUTABLE"
  force_delete         = true

  image_scanning_configuration {
    scan_on_push = true
  }
}

output "ecr_repository_url" {
  description = "Tanks server Docker image repository"
  value       = aws_ecr_repository.server.repository_url
}