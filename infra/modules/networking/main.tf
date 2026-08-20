# VPC + subnets + NAT shared by the Fargate services (CatalogService,
# PictureService — task 5) and the VPC-attached BFF Lambda (task 9). Nothing
# in this module is itself Fargate- or Lambda-specific; it only lays out the
# network those later tasks attach to:
#   - public subnets: the ALB (task 5.4) and NAT Gateways live here.
#   - private subnets: Fargate tasks and the BFF Lambda's ENIs live here,
#     with outbound internet access via NAT and no inbound path from the
#     public internet.

data "aws_availability_zones" "available" {
  state = "available"
}

locals {
  az_count = min(var.az_count, length(data.aws_availability_zones.available.names))
  azs      = slice(data.aws_availability_zones.available.names, 0, local.az_count)

  # /24s carved out of the /16: 10.0.0.0/24, 10.0.1.0/24, ... for public,
  # 10.0.10.0/24, 10.0.11.0/24, ... for private. The gap between the two
  # ranges leaves room to grow either tier (more AZs, more subnets per tier)
  # without renumbering the other.
  public_subnet_cidrs  = [for i in range(local.az_count) : cidrsubnet(var.vpc_cidr, 8, i)]
  private_subnet_cidrs = [for i in range(local.az_count) : cidrsubnet(var.vpc_cidr, 8, i + 10)]

  # Cost/availability trade-off — see the single_nat_gateway variable.
  nat_count = var.single_nat_gateway ? 1 : local.az_count
}

resource "aws_vpc" "main" {
  cidr_block           = var.vpc_cidr
  enable_dns_support   = true
  enable_dns_hostnames = true

  tags = merge(var.tags, { Name = "${var.project_name}-vpc" })
}

resource "aws_internet_gateway" "main" {
  vpc_id = aws_vpc.main.id

  tags = merge(var.tags, { Name = "${var.project_name}-igw" })
}

resource "aws_subnet" "public" {
  count = local.az_count

  vpc_id                  = aws_vpc.main.id
  cidr_block              = local.public_subnet_cidrs[count.index]
  availability_zone       = local.azs[count.index]
  map_public_ip_on_launch = true

  tags = merge(var.tags, {
    Name = "${var.project_name}-public-${local.azs[count.index]}"
    Tier = "public"
  })
}

resource "aws_subnet" "private" {
  count = local.az_count

  vpc_id            = aws_vpc.main.id
  cidr_block        = local.private_subnet_cidrs[count.index]
  availability_zone = local.azs[count.index]

  tags = merge(var.tags, {
    Name = "${var.project_name}-private-${local.azs[count.index]}"
    Tier = "private"
  })
}

resource "aws_eip" "nat" {
  count = local.nat_count

  domain = "vpc"

  tags = merge(var.tags, { Name = "${var.project_name}-nat-eip-${count.index}" })
}

resource "aws_nat_gateway" "main" {
  count = local.nat_count

  allocation_id = aws_eip.nat[count.index].id
  # Each NAT sits in a public subnet; when single_nat_gateway = true there's
  # only one, in the first AZ, and every private subnet routes through it.
  subnet_id = aws_subnet.public[count.index].id

  tags = merge(var.tags, { Name = "${var.project_name}-nat-${count.index}" })

  depends_on = [aws_internet_gateway.main]
}

resource "aws_route_table" "public" {
  vpc_id = aws_vpc.main.id

  route {
    cidr_block = "0.0.0.0/0"
    gateway_id = aws_internet_gateway.main.id
  }

  tags = merge(var.tags, { Name = "${var.project_name}-public-rt" })
}

resource "aws_route_table_association" "public" {
  count = local.az_count

  subnet_id      = aws_subnet.public[count.index].id
  route_table_id = aws_route_table.public.id
}

resource "aws_route_table" "private" {
  count = local.az_count

  vpc_id = aws_vpc.main.id

  route {
    cidr_block     = "0.0.0.0/0"
    nat_gateway_id = var.single_nat_gateway ? aws_nat_gateway.main[0].id : aws_nat_gateway.main[count.index].id
  }

  tags = merge(var.tags, { Name = "${var.project_name}-private-rt-${count.index}" })
}

resource "aws_route_table_association" "private" {
  count = local.az_count

  subnet_id      = aws_subnet.private[count.index].id
  route_table_id = aws_route_table.private[count.index].id
}

# Gateway endpoints for S3 and DynamoDB: free, and let private-subnet
# workloads (PictureService on Fargate, the BFF Lambda) reach the photo
# bucket and the sidecar table without that traffic going through the NAT
# Gateway — cuts NAT data-processing cost and keeps that traffic off the
# public internet entirely, not just off this VPC.
resource "aws_vpc_endpoint" "s3" {
  vpc_id            = aws_vpc.main.id
  service_name      = "com.amazonaws.${var.aws_region}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = concat([aws_route_table.public.id], aws_route_table.private[*].id)

  tags = merge(var.tags, { Name = "${var.project_name}-s3-endpoint" })
}

resource "aws_vpc_endpoint" "dynamodb" {
  vpc_id            = aws_vpc.main.id
  service_name      = "com.amazonaws.${var.aws_region}.dynamodb"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = concat([aws_route_table.public.id], aws_route_table.private[*].id)

  tags = merge(var.tags, { Name = "${var.project_name}-dynamodb-endpoint" })
}
