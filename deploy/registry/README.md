# Dotori-compatible registry deployment

Dotori speaks the npm registry protocol, so the deployment bundle uses the official
Verdaccio 6 image. Package data is persistent and publishing requires an authenticated
user; account self-registration is disabled.

## Docker Compose

Create a bcrypt htpasswd file outside the repository, then enable the optional profile:

```bash
docker run --rm httpd:2.4-alpine htpasswd -nbB dotori 'strong-password' > /secure/htpasswd
DOTORI_REGISTRY_HTPASSWD_FILE=/secure/htpasswd docker compose --profile registry up -d registry
dotori login --registry http://localhost:4873 --token <verdaccio-token>
```

Use `npm login --registry http://localhost:4873` once to obtain a token compatible with
`dotori login`. The named `registry-data` volume retains packages across container restarts.

## Kubernetes

Provision the authentication secret before applying the manifest:

```bash
kubectl create namespace dotori --dry-run=client -o yaml | kubectl apply -f -
kubectl -n dotori create secret generic dotori-registry-auth --from-file=htpasswd=/secure/htpasswd
kubectl apply -f deploy/registry/kubernetes.yaml
kubectl -n dotori port-forward service/dotori-registry 4873:4873
```

The StatefulSet runs one non-root replica with a 20 GiB `ReadWriteOnce` claim. For horizontal
scaling, replace the filesystem storage backend with a Verdaccio storage plugin that supports
shared/concurrent access. Put TLS and ingress authentication at the cluster ingress layer.
