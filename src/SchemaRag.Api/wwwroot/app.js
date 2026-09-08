(() => {
  const elements = {
    catalogTables: document.querySelector('#catalog-tables'),
    copySql: document.querySelector('#copy-sql'),
    dataSource: document.querySelector('#data-source'),
    discoverButton: document.querySelector('#discover-button'),
    emptyState: document.querySelector('#empty-state'),
    executionTime: document.querySelector('#execution-time'),
    generatedSql: document.querySelector('#generated-sql'),
    sqlSource: document.querySelector('#sql-source'),
    healthLabel: document.querySelector('#health-label'),
    healthPill: document.querySelector('#health-pill'),
    indexButton: document.querySelector('#index-button'),
    notice: document.querySelector('#notice'),
    queryButton: document.querySelector('#query-button'),
    queryForm: document.querySelector('#query-form'),
    question: document.querySelector('#question'),
    resultQuestion: document.querySelector('#result-question'),
    results: document.querySelector('#results'),
    resultsHeading: document.querySelector('#results-heading'),
    retrievedTables: document.querySelector('#retrieved-tables'),
    rowCount: document.querySelector('#row-count'),
    selectAllButton: document.querySelector('#select-all-button'),
    selectionCount: document.querySelector('#selection-count'),
    selectNoneButton: document.querySelector('#select-none-button'),
    tableContainer: document.querySelector('#table-container')
  };

  const state = {
    schemas: []
  };

  function getProblemMessage(body, status) {
    const validationMessages = Object.values(body?.errors ?? {})
      .flat()
      .filter(message => typeof message === 'string');

    return validationMessages[0]
      ?? body?.detail
      ?? body?.title
      ?? `Request failed with HTTP ${status}.`;
  }

  async function fetchJson(url, options = {}) {
    const response = await fetch(url, {
      ...options,
      headers: {
        Accept: 'application/json',
        ...options.headers
      }
    });
    const contentType = response.headers.get('content-type') ?? '';
    const body = contentType.includes('application/json')
      ? await response.json()
      : null;

    if (!response.ok) {
      throw new Error(getProblemMessage(body, response.status));
    }

    return body;
  }

  function setNotice(message, tone = 'info') {
    elements.notice.textContent = message;
    elements.notice.dataset.tone = tone;
    elements.notice.setAttribute('role', tone === 'error' ? 'alert' : 'status');
    elements.notice.setAttribute('aria-live', tone === 'error' ? 'assertive' : 'polite');
    elements.notice.hidden = false;
    if (tone === 'error') {
      elements.notice.focus();
    }
  }

  function clearNotice() {
    elements.notice.hidden = true;
    elements.notice.textContent = '';
    delete elements.notice.dataset.tone;
    elements.notice.setAttribute('role', 'status');
    elements.notice.setAttribute('aria-live', 'polite');
  }

  function setQueryBusy(isBusy) {
    elements.queryButton.disabled = isBusy;
    elements.queryButton.setAttribute('aria-busy', String(isBusy));
    elements.queryButton.firstElementChild.textContent = isBusy ? 'Generating SQL…' : 'Run query';
  }

  function createTextElement(tagName, text, className) {
    const element = document.createElement(tagName);
    element.textContent = text;
    if (className) {
      element.className = className;
    }
    return element;
  }

  function formatCell(value) {
    if (value === null || value === undefined) {
      return 'null';
    }

    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }

  function getSelectedTableIds() {
    return Array.from(elements.catalogTables.querySelectorAll('input[type="checkbox"]:checked'))
      .map(checkbox => checkbox.value);
  }

  function updateSelectionCount() {
    const selectedCount = getSelectedTableIds().length;
    elements.selectionCount.textContent = `${selectedCount} selected`;
    elements.indexButton.disabled = selectedCount === 0;
    elements.queryButton.disabled = selectedCount === 0;
  }

  function renderSchemaSelection(schemas) {
    state.schemas = Array.isArray(schemas) ? [...schemas] : [];
    if (state.schemas.length === 0) {
      elements.catalogTables.replaceChildren(
        createTextElement('span', 'No tables found in the configured schemas.', 'catalog-placeholder')
      );
      updateSelectionCount();
      return;
    }

    const choices = state.schemas.map(schema => {
      const label = document.createElement('label');
      label.className = 'table-choice';
      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.value = schema.tableId ?? '';
      checkbox.checked = true;
      checkbox.addEventListener('change', updateSelectionCount);
      const copy = document.createElement('span');
      copy.append(
        createTextElement('strong', `${schema.schemaName}.${schema.tableName}`),
        createTextElement('small', `${Array.isArray(schema.columns) ? schema.columns.length : 0} columns`)
      );
      label.append(checkbox, copy);
      return label;
    });
    elements.catalogTables.replaceChildren(...choices);
    updateSelectionCount();
  }

  function renderTable(columns, rows) {
    if (columns.length === 0) {
      elements.tableContainer.replaceChildren(
        createTextElement('p', 'The query returned no columns.', 'no-rows')
      );
      return;
    }

    const table = document.createElement('table');
    const caption = createTextElement('caption', 'Generated query results', 'visually-hidden');
    const head = document.createElement('thead');
    const headRow = document.createElement('tr');
    columns.forEach(column => {
      const header = createTextElement('th', column);
      header.scope = 'col';
      headRow.append(header);
    });
    head.append(headRow);

    const body = document.createElement('tbody');
    rows.forEach(row => {
      const tableRow = document.createElement('tr');
      columns.forEach(column => {
        const cell = createTextElement('td', formatCell(row[column]));
        if (row[column] === null || row[column] === undefined) {
          cell.className = 'null-value';
        }
        tableRow.append(cell);
      });
      body.append(tableRow);
    });

    table.append(caption, head, body);
    elements.tableContainer.replaceChildren(
      ...(rows.length === 0
        ? [table, createTextElement('p', 'The query returned no rows.', 'no-rows')]
        : [table])
    );
  }

  function clearResult() {
    elements.results.hidden = true;
    elements.emptyState.hidden = false;
    elements.generatedSql.textContent = '';
    elements.retrievedTables.replaceChildren();
    elements.tableContainer.replaceChildren();
  }

  function describeSqlSource(result) {
    if (result.fromSemanticCache !== true) {
      return 'model';
    }

    return typeof result.cacheSimilarity === 'number'
      ? `cache · ${result.cacheSimilarity.toFixed(2)}`
      : 'cache';
  }

  function renderResult(result) {
    const rows = Array.isArray(result.rows) ? result.rows : [];
    const columns = Array.isArray(result.columns) ? result.columns : [];
    const tables = Array.isArray(result.retrievedTables) ? result.retrievedTables : [];

    elements.rowCount.textContent = String(rows.length);
    elements.executionTime.textContent = `${result.executionTimeMs ?? 0} ms`;
    elements.sqlSource.textContent = describeSqlSource(result);
    elements.sqlSource.classList.toggle('cached', result.fromSemanticCache === true);
    elements.generatedSql.textContent = result.generatedSql ?? '';
    elements.resultQuestion.textContent = result.question ?? '';
    elements.retrievedTables.replaceChildren(
      ...tables.map(table => createTextElement('span', table, 'schema-badge'))
    );
    renderTable(columns, rows);

    elements.emptyState.hidden = true;
    elements.results.hidden = false;
    elements.resultsHeading.focus({ preventScroll: true });
    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    elements.results.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'start' });
  }

  async function checkHealth() {
    try {
      const response = await fetchJson('/health');
      const isHealthy = response?.status === 'healthy';
      elements.healthLabel.textContent = isHealthy ? 'API healthy' : 'API unavailable';
      elements.healthPill.dataset.status = isHealthy ? 'healthy' : 'error';
    } catch {
      elements.healthLabel.textContent = 'API unavailable';
      elements.healthPill.dataset.status = 'error';
    }
  }

  async function loadDataSources() {
    const dataSources = await fetchJson('/api/data-sources');
    const options = (Array.isArray(dataSources) ? dataSources : []).map(source => {
      const option = document.createElement('option');
      option.value = source.id ?? '';
      option.textContent = source.displayName ?? source.id ?? 'Unknown source';
      option.selected = source.isDefault === true;
      return option;
    });
    elements.dataSource.replaceChildren(...options);
    if (options.length === 0) {
      throw new Error('No PostgreSQL data sources are configured.');
    }
  }

  async function discoverSchemas() {
    const dataSourceId = elements.dataSource.value;
    elements.discoverButton.disabled = true;
    elements.catalogTables.replaceChildren(
      createTextElement('span', 'Reading PostgreSQL metadata…', 'catalog-placeholder')
    );

    try {
      const schemas = await fetchJson(
        `/api/data-sources/${encodeURIComponent(dataSourceId)}/schema`
      );
      renderSchemaSelection(schemas);
    } catch (error) {
      state.schemas = [];
      renderSchemaSelection([]);
      setNotice(error instanceof Error ? error.message : 'Schema discovery failed.', 'error');
    } finally {
      elements.discoverButton.disabled = false;
    }
  }

  async function indexSchemas() {
    const tableIds = getSelectedTableIds();
    if (tableIds.length === 0) {
      setNotice('Select at least one table to index.', 'error');
      return;
    }

    clearNotice();
    elements.indexButton.disabled = true;
    elements.indexButton.setAttribute('aria-busy', 'true');
    elements.indexButton.textContent = 'Indexing…';

    try {
      const dataSourceId = elements.dataSource.value;
      const response = await fetchJson(
        `/api/data-sources/${encodeURIComponent(dataSourceId)}/schema/index`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ tableIds })
        }
      );
      setNotice(`${response.indexedCount} schema documents indexed successfully.`, 'success');
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'Schema indexing failed.', 'error');
    } finally {
      elements.indexButton.disabled = false;
      elements.indexButton.setAttribute('aria-busy', 'false');
      elements.indexButton.textContent = 'Index selected tables';
      updateSelectionCount();
    }
  }

  async function runQuery(event) {
    event.preventDefault();
    const question = elements.question.value.trim();
    const tableIds = getSelectedTableIds();

    if (!question) {
      elements.question.focus();
      setNotice('Enter a database question first.', 'error');
      return;
    }

    if (tableIds.length === 0) {
      setNotice('Select at least one indexed table first.', 'error');
      return;
    }

    clearNotice();
    clearResult();
    setQueryBusy(true);
    setNotice('Retrieving schemas and generating a safe query…');

    try {
      const result = await fetchJson('/api/query', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          dataSourceId: elements.dataSource.value,
          tableIds,
          question
        })
      });
      clearNotice();
      renderResult(result);
    } catch (error) {
      setNotice(error instanceof Error ? error.message : 'The query could not be completed.', 'error');
    } finally {
      setQueryBusy(false);
      updateSelectionCount();
    }
  }

  async function copySql() {
    const sql = elements.generatedSql.textContent;
    if (!sql) {
      return;
    }

    try {
      await navigator.clipboard.writeText(sql);
      elements.copySql.textContent = 'Copied';
      window.setTimeout(() => {
        elements.copySql.textContent = 'Copy SQL';
      }, 1500);
    } catch {
      setNotice('Your browser did not allow clipboard access.', 'error');
    }
  }

  function setAllTables(isSelected) {
    elements.catalogTables.querySelectorAll('input[type="checkbox"]').forEach(checkbox => {
      checkbox.checked = isSelected;
    });
    updateSelectionCount();
  }

  document.querySelectorAll('[data-question]').forEach(button => {
    button.addEventListener('click', () => {
      elements.question.value = button.dataset.question ?? '';
      elements.question.focus();
    });
  });
  elements.question.addEventListener('keydown', event => {
    if ((event.metaKey || event.ctrlKey) && event.key === 'Enter') {
      event.preventDefault();
      elements.queryForm.requestSubmit();
    }
  });
  elements.queryForm.addEventListener('submit', runQuery);
  elements.indexButton.addEventListener('click', indexSchemas);
  elements.discoverButton.addEventListener('click', discoverSchemas);
  elements.dataSource.addEventListener('change', discoverSchemas);
  elements.selectAllButton.addEventListener('click', () => setAllTables(true));
  elements.selectNoneButton.addEventListener('click', () => setAllTables(false));
  elements.copySql.addEventListener('click', copySql);

  Promise.all([checkHealth(), loadDataSources()])
    .then(discoverSchemas)
    .catch(error => {
      setNotice(error instanceof Error ? error.message : 'Initialization failed.', 'error');
    });
})();
