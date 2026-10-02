FROM python:3.12-slim
RUN mkdir /gateway /cln /app && chown 1000:1000 /gateway
WORKDIR /app
COPY gateway.py /app/gateway.py
USER 1000:1000
ENV CLN_SOCKET=/cln/lightning-rpc GATEWAY_SOCKET=/gateway/lightning-rpc PYTHONDONTWRITEBYTECODE=1
ENTRYPOINT ["python3", "/app/gateway.py"]
