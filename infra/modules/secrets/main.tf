# Secrets Manager entry for PictureService's Gemini API key (see
# GeminiApiService.cs and CLAUDE.md: "Gemini:ApiKey" / GEMINI_API_KEY
# today). This only declares the secret *container* — no value is stored
# here or anywhere in this repo. After `terraform apply`, set the real
# value once, out of band:
#
#   aws secretsmanager put-secret-value \
#     --secret-id <secret_name output below> \
#     --secret-string '{"Gemini:ApiKey":"...","Gemini:Model":"gemini-2.5-flash"}'
#
# Wiring this secret into PictureService's ECS task definition (as a task
# `secrets` entry, with the task execution role granted
# secretsmanager:GetSecretValue on this ARN) is task 5.2 — out of scope
# here.

resource "aws_secretsmanager_secret" "gemini_api_key" {
  name                    = "${var.project_name}/${var.environment}/gemini-api-key"
  description             = "Gemini API key + model used by PictureService's AI analysis (GeminiApiService.cs). Value is set manually after apply — never committed to this repo."
  recovery_window_in_days = var.recovery_window_in_days

  tags = var.tags
}
