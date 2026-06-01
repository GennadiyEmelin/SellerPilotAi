import { StrictMode } from "react";
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  AlertTriangle,
  BarChart3,
  Bot,
  CheckCircle2,
  KeyRound,
  Link2,
  PackageCheck,
  RefreshCw,
  Save,
  Upload
} from "lucide-react";
import "./styles.css";

type ProductMetrics = {
  sku: string;
  name: string;
  sold: number;
  price: number;
  stock: number;
  grossRevenue: number;
  expenses: number;
  commissionExpense: number;
  logisticsExpense: number;
  servicesExpense: number;
  returnExpense: number;
  profit: number;
  margin: number;
  stockDays: number;
  signal: "risk" | "watch" | "good";
};

type Recommendation = {
  level: "high" | "medium" | "good";
  title: string;
  text: string;
};

type SellerTask = {
  title: string;
  text: string;
};

type DashboardResponse = {
  revenue: number;
  profit: number;
  margin: number;
  riskCount: number;
  products: ProductMetrics[];
  recommendations: Recommendation[];
  tasks: SellerTask[];
  source: "ozon-api" | "ozon-not-connected";
  integrationMessages: string[];
};

type IntegrationStatus = {
  marketplace: string;
  configured: boolean;
  available: boolean;
  message: string;
};

type OzonCredentialsStatus = {
  configured: boolean;
  clientIdPreview: string;
};

const currency = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
  maximumFractionDigits: 0
});

function App() {
  const [dashboard, setDashboard] = useDashboard();
  const [integrations, refreshIntegrations] = useIntegrations();
  const [activeSection, setActiveSection] = useState<"overview" | "integrations">("overview");
  const [filter, setFilter] = useState<"all" | "risk" | "profit">("all");

  if (!dashboard) {
    return (
      <main className="loading">
        <Bot aria-hidden="true" />
        <span>Загружаем SellerPilot AI</span>
      </main>
    );
  }

  const filteredProducts = dashboard.products.filter((product) => {
    if (filter === "risk") return product.signal !== "good";
    if (filter === "profit") return product.profit > 0 && product.signal === "good";
    return true;
  });

  return (
    <main className="app">
      <header className="appHeader">
        <div className="brand">
          <span>SellerPilot</span>
          <strong>AI кабинет продавца</strong>
        </div>

        <nav className="nav" aria-label="Разделы">
          {[
            { key: "overview", label: "Обзор" },
            { key: "integrations", label: "Интеграции" }
          ].map((item) => (
            <button
              className={activeSection === item.key ? "active" : ""}
              key={item.key}
              onClick={() => setActiveSection(item.key as "overview" | "integrations")}
            >
              {item.label}
            </button>
          ))}
        </nav>

        <div className="headerStatus">
          <span>{integrations.some((item) => item.available) ? "Ozon подключен" : "Ozon не подключен"}</span>
        </div>
      </header>

      <section className="workspace">
        {activeSection === "integrations" ? (
          <IntegrationsView
            integrations={integrations}
            refreshIntegrations={refreshIntegrations}
            reloadDashboard={() => setDashboard(null)}
          />
        ) : (
          <>
        <header className="topbar">
          <div>
            <p className="eyebrow">31 мая 2026</p>
            <h1>Что сделать сегодня, чтобы прибыль не просела</h1>
          </div>
          <div className="actions">
            <button className="ghostButton" onClick={() => setDashboard(null)}>
              <BarChart3 aria-hidden="true" />
              Обновить расчет
            </button>
            <button className="primaryButton">
              <Upload aria-hidden="true" />
              Загрузить отчет
            </button>
          </div>
        </header>

        <section className="metrics" aria-label="Ключевые показатели">
          <Metric label="Выручка" value={currency.format(dashboard.revenue)} note="за 30 дней" />
          <Metric
            label="Чистая прибыль"
            value={currency.format(dashboard.profit)}
            note={dashboard.profit > 0 ? "бизнес в плюсе" : "нужно срочно резать расходы"}
          />
          <Metric label="Средняя маржа" value={`${Math.round(dashboard.margin * 100)}%`} note="по активным SKU" />
          <Metric label="Рискованные SKU" value={String(dashboard.riskCount)} note="требуют решения" warning />
        </section>

        <section className="integrations">
          <div>
            <p className="eyebrow">Интеграция</p>
            <h2>Ozon Seller API</h2>
          </div>
          <div className="integrationList">
            {integrations.map((item) => (
              <div className={`integration ${item.available ? "online" : "offline"}`} key={item.marketplace}>
                <Link2 aria-hidden="true" />
                <span>
                  <strong>{item.marketplace}</strong>
                  <small>{item.message}</small>
                </span>
              </div>
            ))}
          </div>
          <button className="ghostButton" onClick={refreshIntegrations}>
            <RefreshCw aria-hidden="true" />
            Проверить
          </button>
        </section>

        <section className="grid">
          <article className="panel">
            <SectionHeading eyebrow="AI-помощник" title="Рекомендации на сегодня" live />
            <div className="recommendations">
              {dashboard.recommendations.map((item) => (
                <div className={`recommendation ${item.level}`} key={item.title}>
                  <strong>{item.title}</strong>
                  <p>{item.text}</p>
                </div>
              ))}
            </div>
          </article>

          <article className="panel">
            <SectionHeading eyebrow="Операционка" title="Задачи" />
            <div className="tasks">
              {dashboard.tasks.map((task) => (
                <label className="task" key={task.title}>
                  <input type="checkbox" />
                  <span>
                    <strong>{task.title}</strong>
                    <p>{task.text}</p>
                  </span>
                </label>
              ))}
            </div>
          </article>
        </section>

        <section className="panel">
          <div className="sectionHeading">
            <div>
              <p className="eyebrow">SKU</p>
              <h2>Прибыль по товарам</h2>
            </div>
            <div className="filters" role="group" aria-label="Фильтр товаров">
              <button className={filter === "all" ? "active" : ""} onClick={() => setFilter("all")}>
                Все
              </button>
              <button className={filter === "risk" ? "active" : ""} onClick={() => setFilter("risk")}>
                Риск
              </button>
              <button className={filter === "profit" ? "active" : ""} onClick={() => setFilter("profit")}>
                Прибыль
              </button>
            </div>
          </div>
          <ProductTable products={filteredProducts} />
        </section>
          </>
        )}
      </section>
    </main>
  );
}

function useDashboard(): [DashboardResponse | null, (value: DashboardResponse | null) => void] {
  const [dashboard, setDashboard] = useState<DashboardResponse | null>(null);

  useEffect(() => {
    if (dashboard !== null) return;

    let alive = true;
    fetch("/api/dashboard")
      .then((response) => {
        if (!response.ok) throw new Error("Dashboard request failed");
        return response.json() as Promise<DashboardResponse>;
      })
      .then((data) => {
        if (alive) setDashboard(data);
      })
      .catch(() => {
        if (alive) setDashboard(fallbackDashboard);
      });

    return () => {
      alive = false;
    };
  }, [dashboard]);

  return [dashboard, setDashboard];
}

function useIntegrations(): [IntegrationStatus[], () => void] {
  const [version, setVersion] = useState(0);
  const [integrations, setIntegrations] = useState<IntegrationStatus[]>([]);

  useEffect(() => {
    let alive = true;
    fetch("/api/integrations/status")
      .then((response) => {
        if (!response.ok) throw new Error("Integrations request failed");
        return response.json() as Promise<IntegrationStatus[]>;
      })
      .then((data) => {
        if (alive) setIntegrations(data);
      })
      .catch(() => {
        if (alive) {
          setIntegrations([
            { marketplace: "Ozon", configured: false, available: false, message: "backend недоступен" },
          ]);
        }
      });

    return () => {
      alive = false;
    };
  }, [version]);

  return [integrations, () => setVersion((current) => current + 1)];
}

function IntegrationsView({
  integrations,
  refreshIntegrations,
  reloadDashboard
}: {
  integrations: IntegrationStatus[];
  refreshIntegrations: () => void;
  reloadDashboard: () => void;
}) {
  const [clientId, setClientId] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [status, setStatus] = useState<OzonCredentialsStatus | null>(null);
  const [message, setMessage] = useState("");
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    let alive = true;
    fetch("/api/integrations/ozon/credentials")
      .then((response) => {
        if (!response.ok) throw new Error("Credentials request failed");
        return response.json() as Promise<OzonCredentialsStatus>;
      })
      .then((data) => {
        if (alive) setStatus(data);
      })
      .catch(() => {
        if (alive) setMessage("Backend недоступен. Запустите сервер и повторите.");
      });

    return () => {
      alive = false;
    };
  }, [saving]);

  const saveCredentials = async () => {
    setSaving(true);
    setMessage("");

    try {
      const response = await fetch("/api/integrations/ozon/credentials", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ clientId, apiKey })
      });

      if (!response.ok) throw new Error(await response.text());

      const data = (await response.json()) as OzonCredentialsStatus;
      setStatus(data);
      setClientId("");
      setApiKey("");
      setMessage("Ключи сохранены локально. Теперь можно проверить подключение.");
      refreshIntegrations();
      reloadDashboard();
    } catch (error) {
      setMessage(error instanceof Error ? error.message : "Не удалось сохранить ключи.");
    } finally {
      setSaving(false);
    }
  };

  return (
    <section className="settingsPage">
      <header className="topbar">
        <div>
          <p className="eyebrow">Настройки</p>
          <h1>Подключение Ozon Seller API</h1>
        </div>
      </header>

      <section className="settingsGrid">
        <article className="panel">
          <SectionHeading eyebrow="Ключи" title="Доступ к кабинету Ozon" />
          <div className="credentialsForm">
            <label>
              <span>Client-Id</span>
              <input value={clientId} onChange={(event) => setClientId(event.target.value)} placeholder="Например: 123456" />
            </label>
            <label>
              <span>Api-Key</span>
              <input
                value={apiKey}
                onChange={(event) => setApiKey(event.target.value)}
                placeholder="Вставьте API-ключ"
                type="password"
              />
            </label>
            <button className="primaryButton" onClick={saveCredentials} disabled={saving}>
              <Save aria-hidden="true" />
              {saving ? "Сохраняем" : "Сохранить ключи"}
            </button>
            {message ? <p className="formMessage">{message}</p> : null}
          </div>
        </article>

        <article className="panel">
          <SectionHeading eyebrow="Статус" title="Проверка подключения" />
          <div className="settingsStatus">
            <div className={`credentialBadge ${status?.configured ? "online" : "offline"}`}>
              <KeyRound aria-hidden="true" />
              <span>
                <strong>{status?.configured ? "Ключи сохранены" : "Ключи не сохранены"}</strong>
                <small>{status?.clientIdPreview ? `Client-Id: ${status.clientIdPreview}` : "Данные хранятся только локально на этом компьютере."}</small>
              </span>
            </div>
            {integrations.map((item) => (
              <div className={`integration ${item.available ? "online" : "offline"}`} key={item.marketplace}>
                <Link2 aria-hidden="true" />
                <span>
                  <strong>{item.marketplace}</strong>
                  <small>{item.message}</small>
                </span>
              </div>
            ))}
            <button className="ghostButton" onClick={refreshIntegrations}>
              <RefreshCw aria-hidden="true" />
              Проверить Ozon
            </button>
          </div>
        </article>
      </section>
    </section>
  );
}

function Metric({ label, value, note, warning = false }: { label: string; value: string; note: string; warning?: boolean }) {
  return (
    <article className={`metric ${warning ? "warning" : ""}`}>
      <span>{label}</span>
      <strong>{value}</strong>
      <small>{note}</small>
    </article>
  );
}

function SectionHeading({ eyebrow, title, live = false }: { eyebrow: string; title: string; live?: boolean }) {
  return (
    <div className="sectionHeading">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      {live ? <span className="statusDot">live</span> : null}
    </div>
  );
}

function ProductTable({ products }: { products: ProductMetrics[] }) {
  if (products.length === 0) {
    return (
      <div className="emptyState">
        <strong>Товары Ozon пока не загружены</strong>
        <p>Проверьте ключи Ozon API и права доступа, затем обновите расчет.</p>
      </div>
    );
  }

  return (
    <div className="tableWrap">
      <table>
        <thead>
          <tr>
            <th>Товар</th>
            <th>Артикул</th>
            <th>Продано</th>
            <th>Выручка</th>
            <th>Комиссия</th>
            <th>Логистика</th>
            <th>Услуги</th>
            <th>Расходы</th>
            <th>Прибыль</th>
            <th>Маржа</th>
            <th>Остаток</th>
            <th>Сигнал</th>
          </tr>
        </thead>
        <tbody>
          {products.map((product) => (
            <tr key={product.sku}>
              <td>{product.name}</td>
              <td>{product.sku}</td>
              <td>{product.sold}</td>
              <td>{currency.format(product.grossRevenue)}</td>
              <td>{currency.format(product.commissionExpense)}</td>
              <td>{currency.format(product.logisticsExpense)}</td>
              <td>{currency.format(product.servicesExpense + product.returnExpense)}</td>
              <td>{currency.format(product.expenses)}</td>
              <td className={product.profit >= 0 ? "positive" : "negative"}>{currency.format(product.profit)}</td>
              <td>{Math.round(product.margin * 100)}%</td>
              <td>
                {product.stock} шт. / {Math.round(product.stockDays)} дн.
              </td>
              <td>
                <SignalBadge signal={product.signal} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SignalBadge({ signal }: { signal: ProductMetrics["signal"] }) {
  const config = {
    risk: { label: "Риск", icon: AlertTriangle },
    watch: { label: "Контроль", icon: PackageCheck },
    good: { label: "Здорово", icon: CheckCircle2 }
  }[signal];
  const Icon = config.icon;

  return (
    <span className={`tag ${signal}`}>
      <Icon aria-hidden="true" />
      {config.label}
    </span>
  );
}

const fallbackDashboard: DashboardResponse = {
  revenue: 0,
  profit: 0,
  margin: 0,
  riskCount: 0,
  source: "ozon-not-connected",
  integrationMessages: ["Backend API недоступен"],
  recommendations: [
    {
      level: "medium",
      title: "Ozon API не подключен",
      text: "Запустите ASP.NET Core API и добавьте ключи Ozon."
    }
  ],
  tasks: [
    { title: "Запустить backend", text: "Выполните dotnet run --no-restore --project server --urls http://localhost:5106." }
  ],
  products: []
};

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>
);
