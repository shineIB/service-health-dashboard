# Running in Kubernetes (minikube)

[← Back to README](../README.md)

Everything lives in the `service-health-dashboard` namespace — pass `-n service-health-dashboard` (or `kubectl -n service-health-dashboard ...`) for every command below. Standalone from the Docker Compose instructions in the README — clone first if you haven't already:

```bash
git clone https://github.com/shineIB/service-health-dashboard.git
cd service-health-dashboard

# Build and load the four images (no external registry)
# GIT_SHA/BUILD_TIME are baked in as build args so the dashboard's "version"
# column shows something other than "unknown" — see Dockerfile comments.
GIT_SHA=$(git rev-parse --short HEAD)
BUILD_TIME=$(date -u +%Y-%m-%dT%H:%M:%SZ)
docker build -t orders-service:local        -f src/OrdersService/OrdersService.Api/Dockerfile        --build-arg GIT_SHA=$GIT_SHA --build-arg BUILD_TIME=$BUILD_TIME .
docker build -t inventory-service:local     -f src/InventoryService/InventoryService.Api/Dockerfile     --build-arg GIT_SHA=$GIT_SHA --build-arg BUILD_TIME=$BUILD_TIME .
docker build -t notifications-service:local -f src/NotificationsService/NotificationsService.Api/Dockerfile --build-arg GIT_SHA=$GIT_SHA --build-arg BUILD_TIME=$BUILD_TIME .
docker build -t dashboard-service:local     -f src/DashboardService/DashboardService.Api/Dockerfile     --build-arg GIT_SHA=$GIT_SHA --build-arg BUILD_TIME=$BUILD_TIME .

minikube start
minikube image load orders-service:local
minikube image load inventory-service:local
minikube image load notifications-service:local
minikube image load dashboard-service:local

# Namespace, then Postgres (Deployment + PVC) for both services
kubectl apply -f k8s/namespace.yaml
kubectl -n service-health-dashboard apply \
  -f k8s/orders-service/postgres-secret.yaml -f k8s/orders-service/postgres-pvc.yaml \
  -f k8s/orders-service/postgres-deployment.yaml -f k8s/orders-service/postgres-service.yaml \
  -f k8s/inventory-service/postgres-secret.yaml -f k8s/inventory-service/postgres-pvc.yaml \
  -f k8s/inventory-service/postgres-deployment.yaml -f k8s/inventory-service/postgres-service.yaml \
  -f k8s/orders-service/secret.yaml -f k8s/inventory-service/secret.yaml

kubectl -n service-health-dashboard rollout status deployment/orders-postgres
kubectl -n service-health-dashboard rollout status deployment/inventory-postgres

# Jaeger (traces UI) and RabbitMQ — nothing blocks on either being up first (orders-service's
# outbox dispatcher and notifications-service's consumer both retry their own connection), but
# they need to exist before the services that depend on them start doing anything useful with them
kubectl -n service-health-dashboard apply -f k8s/jaeger/deployment.yaml -f k8s/jaeger/service.yaml
kubectl -n service-health-dashboard apply -f k8s/rabbitmq/deployment.yaml -f k8s/rabbitmq/service.yaml
kubectl -n service-health-dashboard rollout status deployment/jaeger
kubectl -n service-health-dashboard rollout status deployment/rabbitmq

# Migrations run as one-shot Jobs, not on pod startup — see "Architecture decisions" in README.md
kubectl -n service-health-dashboard apply -f k8s/orders-service/migration-job.yaml -f k8s/inventory-service/migration-job.yaml
kubectl -n service-health-dashboard wait --for=condition=complete job/orders-migrate --timeout=120s
kubectl -n service-health-dashboard wait --for=condition=complete job/inventory-migrate --timeout=120s

# The services themselves, plus the dashboard
kubectl -n service-health-dashboard apply \
  -f k8s/orders-service/deployment.yaml -f k8s/orders-service/service.yaml \
  -f k8s/inventory-service/deployment.yaml -f k8s/inventory-service/service.yaml \
  -f k8s/notifications-service/deployment.yaml -f k8s/notifications-service/service.yaml \
  -f k8s/dashboard-service/deployment.yaml -f k8s/dashboard-service/service.yaml

kubectl -n service-health-dashboard rollout status deployment/orders-service
kubectl -n service-health-dashboard rollout status deployment/inventory-service
kubectl -n service-health-dashboard rollout status deployment/notifications-service
kubectl -n service-health-dashboard rollout status deployment/dashboard-service

# Prometheus (scrapes the four app pods above via their prometheus.io/* annotations —
# see "Metrics dashboards" in README.md) and Grafana (dashboard provisioned from k8s/grafana/,
# not clicked together by hand)
kubectl -n service-health-dashboard apply \
  -f k8s/prometheus/rbac.yaml -f k8s/prometheus/configmap.yaml \
  -f k8s/prometheus/deployment.yaml -f k8s/prometheus/service.yaml
kubectl -n service-health-dashboard rollout status deployment/prometheus

kubectl -n service-health-dashboard apply \
  -f k8s/grafana/datasource-configmap.yaml -f k8s/grafana/dashboard-provider-configmap.yaml \
  -f k8s/grafana/dashboard-configmap.yaml -f k8s/grafana/deployment.yaml -f k8s/grafana/service.yaml
kubectl -n service-health-dashboard rollout status deployment/grafana
```

## Opening the UIs

Each of these opens a tunnel and blocks the terminal it runs in — on the Docker driver (Windows/macOS) `minikube service` doesn't return, it just prints a URL and waits. **Run each in its own terminal window**, not appended to the block above: pasting all five into one terminal only gets you the first tunnel, since the rest of the paste just sits unread until you `Ctrl+C` it.

```bash
minikube service dashboard-service -n service-health-dashboard
```

```bash
minikube service jaeger -n service-health-dashboard
```

```bash
minikube service rabbitmq -n service-health-dashboard
```

```bash
minikube service prometheus -n service-health-dashboard
```

```bash
minikube service grafana -n service-health-dashboard
```

**Reapplying `k8s/grafana/dashboard-configmap.yaml` after editing the dashboard JSON needs a
pod restart to take effect** (`kubectl -n service-health-dashboard rollout restart
deployment/grafana`) — it's mounted with `subPath`, and kubelet does not live-update `subPath`
ConfigMap mounts the way it does whole-directory mounts.

## Troubleshooting: `minikube image load` silently keeping a stale image

All four images are tagged with a fixed tag (`:local`), not a per-build tag
like a git SHA — deliberate for a local dev loop, but it has a sharp edge:
after you rebuild an image (`docker build -t orders-service:local ...`) and
run `minikube image load orders-service:local` again, the node can keep
serving the *old* image contents under that same tag. Combined with
`imagePullPolicy: IfNotPresent` on every Deployment (see "Images" in
`CLAUDE.md`), kubelet sees a tag it already has and never re-pulls — a
`kubectl rollout restart` just restarts pods running your old code, and the
symptom looks like your code change "didn't take" even though the rollout
reports success.

The fix is to remove the tag from the node before reloading, not just
rebuild it:

```bash
minikube image rm orders-service:local
minikube image load orders-service:local
kubectl -n service-health-dashboard rollout restart deployment/orders-service
```

If `minikube image rm` fails with `conflict: unable to remove repository
reference "...:local" (must force) - container ... is using its referenced
image ...`, a running pod is still holding that image — `image rm` silently
does nothing useful in that case, and the subsequent `image load` keeps
serving the stale content even though it reports success. Scale the
Deployment to 0 first so nothing on the node references the old image, then
rm/load/scale back up:

```bash
kubectl -n service-health-dashboard scale deployment/orders-service --replicas=0
minikube image rm orders-service:local
minikube image load orders-service:local
kubectl -n service-health-dashboard scale deployment/orders-service --replicas=1
```

If you're not sure whether the node is actually holding a stale image,
`minikube image ls` lists what the node currently has — compare its age/ID
there against your local `docker images` before spending time debugging the
application itself.
