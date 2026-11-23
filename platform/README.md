# API секции «Крылья»

Локальная версия платформы секции. ASP.NET Core 10, EF Core и PostgreSQL обслуживают Vue-интерфейс. Рейтинг и архив доступны без входа. Панели и кабинет пилота получают данные из API.

## Роли

| Роль | Доступ |
| --- | --- |
| Посетитель | Текущий рейтинг и архив |
| Ученик | Собственный учебный путь, попытки, практика и звание |
| Тренер | Общий список учеников, создание аккаунтов, расписание, посещения, запись и решение по учебным попыткам, ручное открытие ближайшего следующего задания, ввод кругов в месячный зачёт |
| Администратор | Действия тренера, настройка справочников, создание и редактирование испытаний, заданий и званий, смена пароля ученика и управление показом имени |

Новый ученик скрыт в публичных таблицах по умолчанию. После оформления отдельного согласия администратор может включить показ имени. При отключении вся строка пропадает из текущей таблицы и публичного архива. Места и отрывы рассчитываются по общему зачёту и сохраняются после поиска или изменения видимости. Смена пароля отзывает предыдущие сеансы ученика.

## Локальный запуск

Команды ниже выполняются в папке `platform`. Нужны .NET 10 SDK и Node.js 24. Для БД подойдёт свой PostgreSQL или контейнер из `compose.yml`. Для запуска контейнера включите Docker Desktop.

```powershell
if (!(Test-Path .env)) {
    "KRYLYA_DB_PASSWORD=$([guid]::NewGuid().ToString('N'))" | Set-Content .env
}
$dbPassword = ((Get-Content .env | Where-Object { $_ -like 'KRYLYA_DB_PASSWORD=*' }) -split '=', 2)[1]
$env:ConnectionStrings__Krylya = "Host=127.0.0.1;Port=54329;Database=krylya;Username=krylya;Password=$dbPassword"
docker compose -p krylya-platform up -d --wait
```

Файл `.env` исключён из Git. При повторном запуске используйте тот же файл и том Docker. Для создания администратора передайте логин и пароль длиной от 12 символов через `Bootstrap__Login` и `Bootstrap__Password`.

```powershell
dotnet run --project Krylya.Api -- --bootstrap-admin
# Перед командой ниже задайте другую пару логина и пароля для тренера.
dotnet run --project Krylya.Api -- --bootstrap-coach
Remove-Item Env:Bootstrap__Login, Env:Bootstrap__Password
# После миграции выдайте ограниченные права приложению.
$runtimePath = '.local/runtime-password'
New-Item -ItemType Directory -Force .local | Out-Null
if (!(Test-Path $runtimePath)) { [guid]::NewGuid().ToString('N') | Set-Content $runtimePath }
$env:KRYLYA_RUNTIME_PASSWORD = (Get-Content $runtimePath -Raw).Trim()
Get-Content -Raw ops/runtime-role.sql | docker compose -p krylya-platform exec -T -e KRYLYA_RUNTIME_PASSWORD postgres psql -X -U krylya -d krylya
$env:ConnectionStrings__Krylya = "Host=127.0.0.1;Port=54329;Database=krylya;Username=krylya_app;Password=$env:KRYLYA_RUNTIME_PASSWORD"
npm ci --prefix frontend
npm run build --prefix frontend
$env:Frontend__Directory = (Resolve-Path frontend/dist).Path
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project Krylya.Api --no-launch-profile --urls http://127.0.0.1:5088
```

Платформа открывается на [127.0.0.1:5088](http://127.0.0.1:5088/). Команды создания аккаунтов применяют миграции. Повтор с тем же логином и ролью сохраняет существующий аккаунт и пароль. API проверяет соединение с PostgreSQL через `/health`. В режиме Development описание маршрутов доступно на `/openapi/v1.json`.

Для редактирования Vue запустите `npm run dev --prefix frontend` во втором терминале. Vite проксирует `/api` на ASP.NET Core на порту 5088.

## Сессия и запросы

Получите `GET /api/auth/csrf`, сохранив cookie. Отправьте значение `token` в заголовке `X-CSRF-Token` у `POST /api/auth/login` с JSON-полями `login` и `password`. После входа получите новый токен тем же GET и используйте его с cookie для всех изменяющих запросов. Для Vue подходят обычные запросы `fetch` к тому же origin. В production cookies требуют HTTPS. При развёртывании ключи ASP.NET Core Data Protection следует хранить в постоянном хранилище.

Передавайте один UUID в `operationId` при сохранении посещения, учебной попытки или круга. При сетевом повторе используйте исходный UUID. Для сетки расписания `week` передаётся как дата понедельника в формате `YYYY-MM-DD`. Часы слота относятся к Москве.

Фоновая задача запускается при старте API и затем каждые 15 секунд. В 00:00 первого числа по Москве закрывается запись кругов за прошлый месяц. Задача фиксирует лучшие времена, места и отрывы. Если испытание текущего месяца подготовлено вручную, оно получает приоритет. Иначе сервер выбирает одну активную пару из раздела «Учебные испытания → Автомобили, трассы и допустимые пары». Поправки в справочнике действуют на будущую жеребьёвку. Если активных пар нет, текущий зачёт появится после настройки пары или ручного создания.

Условия испытания редактируются до записи первого круга. PNG автомобиля и трассы загружаются независимо, сервер проверяет размер до 8 МБ, формат и декодирование. После закрытия месяца итоговые строки, регламент и изображения хранятся в архиве. Редактирование учебного задания сохраняет условия, записанные в уже существующих попытках. Ручное открытие следующего задания само по себе не меняет звание и не завершает предыдущее.

## Звания учеников

Новичок → Пилот I → Пилот II → Пилот III.

- **Новичок** — стартовое звание каждого ученика.
- **Пилот I** — тренер подтвердил выполнение всех пяти заданий начальной программы.
- **Пилот II** и **Пилот III** — следующие ступени. Условия присвоения предстоит определить.

Звание **«Пилот команды»** предназначено для учеников, которые представляют «Крылья» на соревнованиях.

## Развёртывание по HTTPS

`compose.host.yml` запускает PostgreSQL, приложение и Caddy. API и PostgreSQL доступны внутри сети Compose. Caddy запрашивает сертификат для полного имени хоста. Сначала укажите DNS на сервер и подготовьте `.env.host` по `.env.host.example`. Храните реальные пароли вне репозитория. На сервере откройте входящие порты 80 и 443 для Caddy.

```bash
docker compose --env-file .env.host -f compose.host.yml build api migrate
docker compose --env-file .env.host -f compose.host.yml up -d --wait postgres
docker compose --env-file .env.host -f compose.host.yml run --rm migrate
# Передайте KRYLYA_RUNTIME_PASSWORD в окружение из защищённого файла хоста.
docker compose --env-file .env.host -f compose.host.yml exec -T -e KRYLYA_RUNTIME_PASSWORD postgres psql -X -U krylya -d krylya < ops/runtime-role.sql
docker compose --env-file .env.host -f compose.host.yml run --rm -e Bootstrap__Login -e Bootstrap__Password migrate --bootstrap-admin
docker compose --env-file .env.host -f compose.host.yml up -d
```

Перед командой создания аккаунта экспортируйте `Bootstrap__Login` и `Bootstrap__Password` в окружение оболочки, затем удалите их оттуда. Для второго тренера повторите команду с другим логином и аргументом `--bootstrap-coach`. Проверяйте `/health` и вход через HTTPS. При обновлении сначала сделайте резервную копию, остановите API, соберите новый образ, примените миграции и запустите API.

### Резервное копирование

В `database-snapshot.7z` находится заполненная база секции. Для распаковки в 7-Zip нужен тот же ключ, что и для архива временных паролей. На компьютере владельца ключ хранится в `.local/github-password-archive.key`. Распакуйте `krylya.dump` в `.local`.

Чтобы восстановить снимок, запустите PostgreSQL и выберите пустую базу. Пример для PowerShell 7 из каталога `platform` создаёт отдельную базу `krylya_restored` и проверяет журнал миграций.

```powershell
docker compose -p krylya-platform up -d --wait postgres
docker compose -p krylya-platform exec -T postgres createdb -U krylya -T template0 krylya_restored
Get-Content .local/krylya.dump -AsByteStream -Raw |
    docker compose -p krylya-platform exec -T postgres pg_restore --exit-on-error --no-owner --no-acl -U krylya -d krylya_restored
docker compose -p krylya-platform exec -T postgres psql -U krylya -d krylya_restored -c 'SELECT count(*) FROM "__EFMigrationsHistory"'
```

Перед запуском API укажите `Database=krylya_restored` в строке подключения. Примените доступные миграции командой `--migrate` под ролью развёртывания и выдайте права роли приложения по инструкции локального запуска, заменив в команде с `ops/runtime-role.sql` параметр `-d krylya` на `-d krylya_restored`.

В Linux-оболочке выгрузите PostgreSQL в сжатом формате. Храните копию вне сервера с ограниченным доступом. Проверяйте восстановление на отдельной пустой базе.

```bash
umask 077
docker compose --env-file .env.host -f compose.host.yml exec -T postgres pg_dump -U krylya -d krylya -Fc > krylya-backup.dump
docker compose --env-file .env.host -f compose.host.yml exec -T postgres createdb -U krylya -T template0 krylya_restore_check
docker compose --env-file .env.host -f compose.host.yml exec -T postgres pg_restore --exit-on-error -U krylya -d krylya_restore_check < krylya-backup.dump
```

В PowerShell 7.4+ бинарный вывод `pg_dump` можно перенаправить в файл оператором `>`. Для восстановления на Windows скопируйте файл в контейнер через `docker compose cp` и передайте путь внутри контейнера аргументом `pg_restore`. Том ключей Data Protection сохраняет сеансы при штатном обновлении. Согласуйте перед запуском владельца резервных копий и порядок исправления результатов уже закрытых месяцев.

### Сопровождение

После каждой миграции повторите применение ops/runtime-role.sql. Работающий API использует роль krylya_app. Для отключения сотрудника задайте Bootstrap__Login и выполните команду сервиса api с аргументом --disable-staff. Для восстановления тренера или администратора передайте также новый Bootstrap__Password и аргумент --recover-staff. В Compose используйте run --rm -e Bootstrap__Login -e Bootstrap__Password api перед аргументом. Обе команды отзывают старые сеансы.

В панели тренера откройте ученика и «История ученика». Администратор может аннулировать текущий круг или учебную попытку с обязательной причиной. Аннулированный ввод остаётся в истории. Закрытый месяц сохраняет зафиксированные места. Для занятий по расписанию используйте отметку внутри карточки занятия.

Проверка изменений на отдельной БД с вымышленными данными из корня проекта: `pwsh -File platform/tests/run.ps1`. Используется порт 5096. Файл с именами учеников не нужен.

Для Linux-хоста подготовлены ops/backup.sh и ops/monitor.sh. Первый выгружает базу на отдельно смонтированный носитель и проверяет восстановление перед публикацией файла. Второй проверяет HTTPS, состояние архива месяца через /health и наличие успешно проверенной копии за последние 26 часов. В crontab владельца сервиса задайте действительные пути, домен и адрес получателя. Доставка MAILTO требует настроенной почты на хосте. Проверьте получение сообщения при тестовом сбое.

```cron
MAILTO=operations@example.org
KRYLYA_HOST=simracing.example.org
KRYLYA_BACKUP_MOUNT=/mnt/krylya-backups
15 2 * * * bash /srv/krylya/platform/ops/backup.sh
*/5 * * * * bash /srv/krylya/platform/ops/monitor.sh || echo "Проверьте HTTPS, закрытие месяца и резервные копии Крыльев"
```
