# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a full-stack ERP (Enterprise Resource Planning) application called 博足ERP (JshERP). It consists of:

- **Backend**: erp-boot - Java Spring Boot 2.0.0 with MyBatis-Plus
- **Frontend**: erp-web - Vue.js 2.7.16 with Ant Design Vue 1.5.2
- **Database**: MySQL 8.0.24
- **Cache**: Redis 6.2.1
- **Deployment**: Docker, Nginx

## Common Commands

### Frontend (erp-web)
```bash
cd erp-web
yarn install          # Install dependencies
yarn serve           # Development server
yarn build           # Production build
```

### Backend (erp-boot)
```bash
cd erp-boot
mvn clean package    # Build JAR
java -jar target/erp-boot-3.6-SNAPSHOT.jar  # Run
```

### Docker Deployment
```bash
docker-compose up -d    # Start all services
```

## Architecture

### Backend Structure (erp-boot)
```
erp-boot/src/main/java/com/jsh/erp/
├── ErpApplication.java          # Main entry point
├── base/                        # Base classes (AjaxResult, BaseController, etc.)
├── config/                      # Configuration (plugins, tenant)
├── constants/                   # Business & exception constants
├── controller/                 # REST controllers (40+ controllers)
├── datasource/
│   ├── entities/              # MyBatis entity classes
│   └── mappers/               # MyBatis mappers
├── service/                   # Business logic
├── utils/                     # Utilities
└── plugin/                    # Plugin system (springboot-plugin-framework)
```

### Frontend Structure (erp-web)
```
erp-web/src/
├── main.js                    # Entry point
├── App.vue                    # Root component
├── api/                       # API calls
├── assets/                    # Static assets
├── components/                # Reusable components
├── pages/                     # Page components
├── router/                    # Vue Router config
├── store/                     # Vuex store
└── utils/                     # Utilities
```

### Key Modules (Backend Controllers)
- **Account**: Account management (账户管理)
- **Depot**: Warehouse/inventory (仓库管理)
- **Material**: Product/materials (商品管理)
- **DepotHead**: Inventory transactions (出入库)
- **Customer**: Customer management
- **Supplier**: Supplier management
- **User/Tenant**: Authentication & multi-tenancy

### Cashier Module (New)
Recent development includes a `cashier/` directory for point-of-sale functionality with:
- Service timers
- Commission rules
- Settlement handling
- Cart management

## Database

- Default credentials: tenant `jsh`, username `admin`, password `123456`
- Multi-tenant architecture with tenant isolation
- MyBatis-Plus for ORM with generated Example classes

## Development Notes

- Backend uses springboot-plugin-framework for plugin extensibility
- Frontend uses vue-cli 3.x with webpack
- ESLint is configured (can be disabled in package.json)
- API documentation available at `/swagger-ui.html` when running
