#!/bin/bash
echo "Iniciando configuração do LocalStack..."

# --- WALLET CONTEXT ---
awslocal sqs create-queue --queue-name wallet-queue-dlq
WALLET_DLQ_ARN="arn:aws:sqs:sa-east-1:000000000000:wallet-queue-dlq"

awslocal sqs create-queue --queue-name wallet-queue \
    --attributes RedrivePolicy="{\"deadLetterTargetArn\":\"$WALLET_DLQ_ARN\",\"maxReceiveCount\":\"3\"}"

# --- PURCHASE CONTEXT ---
awslocal sqs create-queue --queue-name purchase-queue-dlq
PURCHASE_DLQ_ARN="arn:aws:sqs:sa-east-1:000000000000:purchase-queue-dlq"

awslocal sqs create-queue --queue-name purchase-queue \
    --attributes RedrivePolicy="{\"deadLetterTargetArn\":\"$PURCHASE_DLQ_ARN\",\"maxReceiveCount\":\"3\"}"

# --- OUTROS ---
awslocal sqs create-queue --queue-name catalog-events-queue
awslocal sns create-topic --name payment-topic

echo "Filas e DLQs criadas com sucesso!"