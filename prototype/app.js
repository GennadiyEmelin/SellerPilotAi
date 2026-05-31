const products = [
  {
    name: "Набор органайзеров для хранения",
    sold: 428,
    price: 790,
    cost: 310,
    commission: 0.18,
    logistics: 92,
    ads: 42000,
    returns: 0.04,
    stock: 78,
    dailySales: 14,
  },
  {
    name: "LED-лента для кухни 3 м",
    sold: 216,
    price: 1190,
    cost: 690,
    commission: 0.17,
    logistics: 126,
    ads: 61500,
    returns: 0.09,
    stock: 19,
    dailySales: 9,
  },
  {
    name: "Косметичка дорожная",
    sold: 352,
    price: 640,
    cost: 260,
    commission: 0.2,
    logistics: 88,
    ads: 25000,
    returns: 0.03,
    stock: 210,
    dailySales: 11,
  },
  {
    name: "Автомобильный держатель телефона",
    sold: 164,
    price: 520,
    cost: 295,
    commission: 0.19,
    logistics: 96,
    ads: 18400,
    returns: 0.07,
    stock: 44,
    dailySales: 6,
  },
  {
    name: "Электронные кухонные весы",
    sold: 91,
    price: 890,
    cost: 530,
    commission: 0.16,
    logistics: 118,
    ads: 16700,
    returns: 0.12,
    stock: 12,
    dailySales: 4,
  },
];

let activeFilter = "all";

const currency = new Intl.NumberFormat("ru-RU", {
  style: "currency",
  currency: "RUB",
  maximumFractionDigits: 0,
});

function calculateSku(item) {
  const grossRevenue = item.sold * item.price;
  const returnLoss = grossRevenue * item.returns;
  const commission = grossRevenue * item.commission;
  const logistics = item.sold * item.logistics;
  const purchaseCost = item.sold * item.cost;
  const expenses = purchaseCost + commission + logistics + item.ads + returnLoss;
  const profit = grossRevenue - expenses;
  const margin = grossRevenue > 0 ? profit / grossRevenue : 0;
  const stockDays = item.dailySales > 0 ? item.stock / item.dailySales : 999;

  return {
    ...item,
    grossRevenue,
    expenses,
    profit,
    margin,
    stockDays,
  };
}

function getSignal(item) {
  if (item.profit < 0 || item.margin < 0.08) {
    return { label: "Риск", type: "risk" };
  }

  if (item.stockDays < 7 || item.margin < 0.18) {
    return { label: "Контроль", type: "watch" };
  }

  return { label: "Здорово", type: "good" };
}

function renderMetrics(items) {
  const revenue = items.reduce((sum, item) => sum + item.grossRevenue, 0);
  const profit = items.reduce((sum, item) => sum + item.profit, 0);
  const margin = revenue > 0 ? profit / revenue : 0;
  const risks = items.filter((item) => getSignal(item).type !== "good").length;

  document.querySelector("#revenueMetric").textContent = currency.format(revenue);
  document.querySelector("#profitMetric").textContent = currency.format(profit);
  document.querySelector("#marginMetric").textContent = `${Math.round(margin * 100)}%`;
  document.querySelector("#riskMetric").textContent = String(risks);
  document.querySelector("#profitDelta").textContent =
    profit > 0 ? "бизнес в плюсе" : "нужно срочно резать расходы";
}

function buildRecommendations(items) {
  const lowMargin = [...items].sort((a, b) => a.margin - b.margin)[0];
  const stockout = [...items].sort((a, b) => a.stockDays - b.stockDays)[0];
  const best = [...items].sort((a, b) => b.profit - a.profit)[0];

  return [
    {
      level: lowMargin.profit < 0 ? "high" : "medium",
      title: `Проверьте цену и рекламу: ${lowMargin.name}`,
      text: `Маржа ${Math.round(lowMargin.margin * 100)}%, прибыль ${currency.format(
        lowMargin.profit,
      )}. Уменьшите рекламу или поднимите цену минимум на ${currency.format(
        Math.max(40, lowMargin.price * 0.08),
      )}.`,
    },
    {
      level: stockout.stockDays < 7 ? "high" : "medium",
      title: `Риск закончить остатки: ${stockout.name}`,
      text: `Остатка хватит примерно на ${Math.max(1, Math.round(stockout.stockDays))} дн. Подготовьте поставку, чтобы не потерять позицию в выдаче.`,
    },
    {
      level: "good",
      title: `Масштабируйте прибыльный SKU: ${best.name}`,
      text: `Товар дал ${currency.format(best.profit)} прибыли. Можно аккуратно увеличить рекламный лимит и проверить допоставку.`,
    },
  ];
}

function renderRecommendations(items) {
  const container = document.querySelector("#recommendations");
  container.innerHTML = buildRecommendations(items)
    .map(
      (item) => `
        <div class="recommendation ${item.level}">
          <strong>${item.title}</strong>
          <p>${item.text}</p>
        </div>
      `,
    )
    .join("");
}

function renderTasks(items) {
  const risky = items.filter((item) => getSignal(item).type !== "good");
  const tasks = [
    {
      title: "Проверить рекламные ставки",
      text: `${risky.length} SKU требуют контроля по марже и расходам.`,
    },
    {
      title: "Собрать поставку",
      text: "Приоритет товарам, где остатка меньше 7 дней продаж.",
    },
    {
      title: "Обновить себестоимость",
      text: "Загрузите свежий закупочный прайс, чтобы расчет прибыли был точным.",
    },
  ];

  document.querySelector("#tasks").innerHTML = tasks
    .map(
      (task) => `
        <label class="task">
          <input type="checkbox" />
          <span>
            <strong>${task.title}</strong>
            <p>${task.text}</p>
          </span>
        </label>
      `,
    )
    .join("");
}

function renderTable(items) {
  const filtered = items.filter((item) => {
    const signal = getSignal(item).type;
    if (activeFilter === "risk") return signal !== "good";
    if (activeFilter === "profit") return item.profit > 0 && signal === "good";
    return true;
  });

  document.querySelector("#skuTable").innerHTML = filtered
    .map((item) => {
      const signal = getSignal(item);
      const profitClass = item.profit >= 0 ? "positive" : "negative";

      return `
        <tr>
          <td>${item.name}</td>
          <td>${item.sold}</td>
          <td>${currency.format(item.grossRevenue)}</td>
          <td>${currency.format(item.expenses)}</td>
          <td class="${profitClass}">${currency.format(item.profit)}</td>
          <td>${Math.round(item.margin * 100)}%</td>
          <td>${item.stock} шт. / ${Math.round(item.stockDays)} дн.</td>
          <td><span class="tag ${signal.type}">${signal.label}</span></td>
        </tr>
      `;
    })
    .join("");
}

function render() {
  const calculated = products.map(calculateSku);
  renderMetrics(calculated);
  renderRecommendations(calculated);
  renderTasks(calculated);
  renderTable(calculated);
}

document.querySelectorAll(".filter").forEach((button) => {
  button.addEventListener("click", () => {
    document.querySelectorAll(".filter").forEach((item) => item.classList.remove("active"));
    button.classList.add("active");
    activeFilter = button.dataset.filter;
    render();
  });
});

document.querySelector("#simulateButton").addEventListener("click", () => {
  products.forEach((item) => {
    const drift = Math.random() * 0.16 - 0.08;
    item.ads = Math.max(0, Math.round(item.ads * (1 + drift)));
    item.sold = Math.max(1, Math.round(item.sold * (1 + drift / 2)));
  });

  render();
});

render();
