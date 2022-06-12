# Telemachus

Telemachus is an application that collects metrics from DCS-gRPC instances
and posts them using OpenTelemetry metrics to an OpenTelemetry collector that
can then post them to a variety of metrics back-ends for your graphing, monitoring
and alarming pleasure.

# Installation

## OpenTelemetry Collector

See this [guide](https://opentelemetry.io/docs/collector/) for getting started
with the Open Telemetry collector. An example Configuration that posts metrics
to the free [Grafana Cloud](http://grafana.com) is below:

```yaml
receivers:
  otlp:
    protocols:
      grpc:

processors:
  batch:

exporters:
  logging:
  prometheusremotewrite:
    # For example: https://12345:afakagwuefygkufg@prometheus-prod-10-prod-us-central-0.grafana.net/api/prom/push
    endpoint: "https://YOUR_ACCOUND_ID:YOUR_APP_TOKEN@THE_PUSH_URL"

service:
 pipelines:
   metrics:
     receivers: [otlp]
     processors: [batch]
     exporters: [prometheusremotewrite, logging]
```

## Telemachus

1. Download Telemachus from URL and extract into a folder of your choice.
2. Modify the `configuration.yaml` file to suit your installation. The file
   has comments that explain the various options.
3. Run the bot using the `Telemachus.exe` or optionally run as a Windows Service
   (See below). For the initial runs we recommend not running as a service
   to make sure everything is setup correctly.

### Install as a windows service

Run the following command in a Powershell window with administrator
permissions, making sure to change the path to point to the correct location.

```
New-Service -Name Telemachus -BinaryPathName C:\YOUR\PATH\TO\Telemachus.exe -Description "Metrics application for DCS-gRPC" -DisplayName "Telemachus" -StartupType Automatic
```
