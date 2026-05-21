.PHONY: api ui db-update test test-unit test-integration test-e2e test-e2e-ui migrate bench

export PATH := $(HOME)/.dotnet/tools:$(PATH)

db-update:
	dotnet ef database update -p src/Transactatrack.Infrastructure -s src/Transactatrack.Api

api:
	dotnet watch --project src/Transactatrack.Api run

ui:
	cd src/Transactatrack.Web && ng serve

test: test-unit test-integration

test-unit:
	dotnet test tests/Transactatrack.UnitTests

test-integration:
	dotnet test tests/Transactatrack.IntegrationTests

test-e2e:
	cd tests/e2e && npx playwright test

test-e2e-ui:
	cd tests/e2e && npx playwright test --ui

migrate:
	dotnet ef migrations add $(name) \
		-p src/Transactatrack.Infrastructure \
		-s src/Transactatrack.Api \
		-o Persistence/Migrations

bench:
	dotnet run --project tools/Transactatrack.LlmBenchmark -- $(ARGS)
