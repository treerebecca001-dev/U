"""Read-only bounded Codex runtime error diagnosis; never accesses browser profiles or input drivers."""
import datetime
import json
import pathlib
import sqlite3
import time

workspace = pathlib.Path(__file__).resolve().parent.parent
database = pathlib.Path('C:/Users/tree/.codex/logs_2.sqlite')
connection = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True, timeout=3)
rows = connection.execute(
    'SELECT id,ts,level,target,feedback_log_body FROM logs '
    'WHERE ts>=? AND feedback_log_body LIKE ? ORDER BY id DESC LIMIT 12',
    (int(time.time()) - 86400, '%failed to write kernel assets%')
).fetchall()
connection.close()
errors = []
needle = 'failed to write kernel assets'
for row_id, timestamp, level, target, body in rows:
    offset = body.find(needle)
    errors.append(dict(id=row_id, timestamp=timestamp, level=level, target=target,
                       errorContext=body[max(0, offset - 160):offset + 900]))
report = dict(time=datetime.datetime.now(datetime.timezone.utc).isoformat(),
              databaseReadOnly=True, browserProfileAccessed=False,
              searched='exact kernel-assets error in last 24 hours, maximum 12 rows',
              errors=errors, runtimeConfigurationChanged=False,
              officialTriageReference='https://developers.openai.com/plugins/deploy/troubleshooting',
              note='Official triage describes collecting logs; no specific kernel path repair was established.')
destination = workspace / 'artifacts/input-events-r15/runtime-diagnosis.json'
destination.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps(dict(rows=len(errors), errors=errors), ensure_ascii=True))
