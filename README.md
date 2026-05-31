# SellerPilot AI

SellerPilot AI is an AI-style operating assistant for marketplace sellers. The product is aimed first at Ozon sellers who need a simple answer to one daily question: what should I do today to protect profit and grow sales?

## MVP positioning

The first version is not another analytics dashboard. It is a decision assistant:

- shows real profit by SKU;
- finds unprofitable products;
- warns about stockouts;
- highlights suspicious cost growth;
- suggests practical actions in plain language;
- prepares daily tasks for the seller or marketplace manager.

## First paid version

Core features:

1. Connect Ozon Seller API.
2. Unit economics per SKU.
3. Profit, margin, ad spend, logistics, and commission tracking.
4. Stock forecast and reorder alerts.
5. Daily AI recommendations.
6. Telegram notifications.
7. Simple seller workspace with task statuses.

## Target customer

Small and mid-size sellers with monthly marketplace turnover from 300,000 RUB to 10,000,000 RUB. These sellers often have revenue, but do not clearly see net profit after commissions, logistics, advertising, storage, returns, and purchase cost.

## Monetization

- Starter: 1,990 RUB/month.
- Growth: 4,990 RUB/month.
- Pro: 9,990 RUB/month.
- Agency: from 29,990 RUB/month.

## Tech stack

- Backend: ASP.NET Core Web API.
- Frontend: React, TypeScript, Vite.
- Current data source: Ozon Seller API. Demo products are not used.

## Ozon API setup

Create API credentials in the Ozon seller cabinet and set environment variables before running the backend:

```powershell
$env:OZON_CLIENT_ID="your-client-id"
$env:OZON_API_KEY="your-api-key"
```

The backend checks Ozon through `POST /v3/product/list` with `Client-Id` and `Api-Key` headers. Dashboard products are loaded from Ozon only. Wildberries is intentionally postponed.

Current Ozon data flow:

1. `POST /v3/product/list` loads product identifiers.
2. `POST /v3/product/info/list` loads names, prices, and stock preview.
3. The dashboard shows only live Ozon products. If keys are missing or invalid, it stays empty and shows an integration message.

Do not put real API keys into committed JSON files. Use environment variables for local development.

## Run locally

Backend:

```powershell
dotnet restore server
dotnet run --no-restore --project server --urls http://localhost:5106
```

Frontend:

```powershell
cd client
npm install
npm run dev
```

Open `http://127.0.0.1:5173`.

## Old static prototype

The first static prototype is kept in `prototype/index.html`.
