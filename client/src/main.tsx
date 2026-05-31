import { StrictMode } from "react";
import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import { AlertTriangle, BarChart3, Bot, CheckCircle2, Link2, PackageCheck, RefreshCw, Upload } from "lucide-react";
import "./styles.css";

type ProductMetrics = {
  sku: string;
  name: string;
  sold: number;
  price: number;
  stock: number;
  grossRevenue: number;
  expenses: number;
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

const currency = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
  maximumFractionDigits: 0
});

function App() {
  const [dashboard, setDashboard] = useDashboard();
  const [integrations, refreshIntegrations] = useIntegrations();
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
      <aside className="sidebar">
        <div className="brand">
          <div className="brandMark">SP</div>
          <div>
            <strong>SellerPilot AI</strong>
            <span>Операционный помощник</span>
          </div>
        </div>

        <nav className="nav" aria-label="Разделы">
          {["Обзор", "Товары", "Поставки", "Реклама", "Задачи"].map((item, index) => (
            <button className={index === 0 ? "active" : ""} key={item}>
              {item}
            </button>
          ))}
        </nav>

        <section className="sidePanel">
          <span>Тариф</span>
          <strong>Growth</strong>
          <p>До 1 000 SKU, прогноз остатков, рекомендации и Telegram-уведомления.</p>
        </section>
      </aside>

      <section className="workspace">
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
