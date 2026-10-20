# InvoiceAnalyzerBackend

Backend service for invoice processing, OCR-based extraction, and AI-powered analysis.

## Overview

InvoiceAnalyzerBackend is a .NET 9 ASP.NET Core Web API designed to process invoice documents, extract key information, and analyze the content using an AI service or local mock workflow.

The project is built for scenarios where invoices are uploaded and processed automatically, with support for:
- PDF and image input
- OCR-like extraction flow
- invoice storage in SQLite
- background job processing
- AI-based analysis and categorization
- REST API integration

## Tech Stack

- .NET 9
- ASP.NET Core Web API
- Entity Framework Core
- SQLite
- Swagger / OpenAPI
- PdfPig for PDF processing
- CORS support for frontend clients
- Optional HTTP AI endpoint integration

## Main Features

- Store invoice data locally with SQLite
- Accept uploaded invoice files
- Execute document processing through background jobs
- Extract invoice metadata using OCR/mock extraction services
- Analyze invoice content for anomalies and accounting signals
- Return structured results through API endpoints
- Support local development with mock AI behavior

## Project Structure

- `InvoiceAnalyzerBackend/Program.cs` - application startup and dependency registration
- `InvoiceAnalyzerBackend/Controllers/` - API endpoints
- `InvoiceAnalyzerBackend/Services/` - OCR and AI-related services
- `InvoiceAnalyzerBackend/Data/` - database context and persistence logic
- `InvoiceAnalyzerBackend/HostedServices/` - background job processing
- `InvoiceAnalyzerBackend/Models/` - domain models
- `InvoiceAnalyzerBackend/appsettings.json` - application settings

## Configuration

The app uses SQLite by default and automatically creates the database when the application starts.

Example configuration:

```json
{
  "Ai": {
    "Endpoint": "http://localhost:5000/mock-extract",
    "ApiKey": "<leave-empty-for-dev-or-store-in-user-secrets>",
    "TimeoutSeconds": 30
  }
}
