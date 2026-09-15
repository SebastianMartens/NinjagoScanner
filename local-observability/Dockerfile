# Thin wrapper around Microsoft's published Aspire Dashboard image, pinned to an exact tag and
# pre-configured for local dev use:
#   - no browser login token (DASHBOARD__FRONTEND__AUTHMODE)
#   - no OTLP API key (DASHBOARD__OTLP__AUTHMODE)
#   - OTLP over HTTP/protobuf enabled (DASHBOARD__OTLP__HTTPENDPOINTURL) - the dashboard only
#     listens for OTLP/gRPC by default, but every service in this repo exports via
#     OtlpExportProtocol.HttpProtobuf / the Python OTLPSpanExporter's http/protobuf exporter
#     (see CatalogService/PictureService/Web Program.cs and picture_service/main.py) so their
#     OTLP traffic can also go to Grafana Cloud's OTLP gateway, which only accepts HTTP/protobuf.
#
# See ../docker-compose.yml for how this is run, and README.md in this directory for how to
# point a locally-running service at it.
FROM mcr.microsoft.com/dotnet/aspire-dashboard:9.0

ENV ASPIRE_ALLOW_UNSECURED_TRANSPORT=true
ENV DASHBOARD__FRONTEND__AUTHMODE=Unsecured
ENV DASHBOARD__OTLP__AUTHMODE=Unsecured
ENV DASHBOARD__OTLP__HTTPENDPOINTURL=http://0.0.0.0:18890

EXPOSE 18888 18889 18890
