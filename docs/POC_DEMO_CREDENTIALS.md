# Credenciais da POC — ambiente demonstrativo público

> **NÃO USAR EM PRODUÇÃO.** Estas credenciais existem apenas para a demonstração local/efêmera deste repositório público. Nenhuma delas pertence à CIJUN, Prefeitura de Jundiaí, RenoveJá, fornecedor, profissional real ou ambiente externo.

MFA padrão da POC: `008026` (pode ser alterado via `JUNDIAI_DEMO_MFA_CODE`).

| Perfil | Usuário | Senha | MFA |
|---|---|---|---|
| Administrador POC | `admin.jundiai` | `Jundiai#008` | sim |
| Gestor municipal | `gestor.saude` | `Gestor#008` | sim |
| Regulador | `regulador.central` | `Regula#008` | sim |
| Médico | `medico.ubs` | `Medico#008` | não |
| Enfermagem | `enfermagem.ubs` | `Enfermagem#008` | não |
| Farmácia | `farmacia.central` | `Farmacia#008` | sim |
| Dentista | `dentista.ubs` | `Dentista#008` | não |
| ACS | `acs.micro01` | `Acs#008` | não |
| Auditor | `auditoria.cijun` | `Audita#008` | sim |

## O que muda em produção

- usuários deixam de ser seed no código;
- senhas/segredos não ficam no repositório;
- autenticação deve ser integrada ao IdP definido para a implantação;
- MFA deve usar segredo individual protegido e lifecycle real;
- tokens/sessões devem ter persistência/revogação/políticas produtivas;
- papéis e vínculos por unidade/instituição devem ser administráveis e auditáveis;
- ambientes POC, homologação e produção devem permanecer isolados.
