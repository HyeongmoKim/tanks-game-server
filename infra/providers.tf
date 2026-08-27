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