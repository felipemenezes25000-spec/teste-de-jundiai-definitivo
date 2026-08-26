# Jundiaí HealthOS — RCE 008/2026

Repositório dedicado à implementação e demonstração da solução para o RCE 008/2026 da CIJUN/Jundiaí.

Este projeto reaproveita componentes técnicos do RenoveJá Public Service, mas mantém uma linha de desenvolvimento independente, orientada à POC e aos requisitos do edital.

## Objetivo

Consolidar em uma única aplicação demonstrável os módulos de saúde pública necessários para a POC, incluindo:

- administração, segurança e auditoria;
- cadastro e identidade SUS;
- recepção e atendimento UBS;
- prontuário eletrônico multiprofissional;
- regulação e agendamento;
- exames laboratoriais e imagem;
- odontologia;
- faturamento SUS;
- vacinação;
- PSF/território/ACS;
- farmácia, estoque e almoxarifado;
- portal/app do cidadão e telemedicina;
- BI e dashboards.

## Regra de origem

Código reaproveitado do RenoveJá Public Service deve ser tratado como base técnica. Recursos dependentes de credenciais/homologações externas continuam fail-closed e não devem simular integração produtiva.
