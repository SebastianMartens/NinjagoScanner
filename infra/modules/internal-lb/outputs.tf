output "dns_name" {
  description = "Internal-only DNS name — resolvable from inside the VPC (including a VPC-attached Lambda), never from the public internet."
  value       = aws_lb.internal.dns_name
}

output "arn" {
  value = aws_lb.internal.arn
}

output "security_group_id" {
  value = aws_security_group.lb.id
}

output "target_group_arns" {
  value = { for name, tg in aws_lb_target_group.this : name => tg.arn }
}
