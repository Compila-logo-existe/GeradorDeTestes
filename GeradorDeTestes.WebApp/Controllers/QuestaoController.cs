using GeradorDeTestes.Dominio.ModuloMateria;
using GeradorDeTestes.Dominio.ModuloQuestao;
using GeradorDeTestes.Dominio.ModuloTeste;
using GeradorDeTestes.Infraestrutura.ORM.Compartilhado;
using GeradorDeTestes.WebApp.Extensions;
using GeradorDeTestes.WebApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore.Storage;

namespace GeradorDeTestes.WebApp.Controllers;

[Route("questoes")]
public class QuestaoController : Controller
{
    private readonly GeradorDeTestesDbContext contexto;
    private readonly IRepositorioMateria repositorioMateria;
    private readonly IRepositorioQuestao repositorioQuestao;
    private readonly IRepositorioTeste repositorioTeste;

    public QuestaoController(GeradorDeTestesDbContext contexto, IRepositorioMateria repositorioMateria,
        IRepositorioQuestao repositorioQuestao, IRepositorioTeste repositorioTeste)
    {
        this.contexto = contexto;
        this.repositorioMateria = repositorioMateria;
        this.repositorioQuestao = repositorioQuestao;
        this.repositorioTeste = repositorioTeste;
    }

    public IActionResult Index()
    {
        List<Questao> questoesNaoFinalizadas = repositorioQuestao.SelecionarNaoFinalizados();

        repositorioQuestao.RemoverRegistros(questoesNaoFinalizadas);

        List<Questao> questoes = repositorioQuestao.SelecionarRegistros()
                                                   .Where(q => q.Finalizado)
                                                   .ToList();

        VisualizarQuestoesViewModel visualizarVM = new(questoes);

        return View(visualizarVM);
    }

    [HttpGet("cadastrar")]
    public IActionResult Cadastrar()
    {
        List<Materia> materias = repositorioMateria.SelecionarRegistros();

        CadastrarQuestaoViewModel cadastrarVM = new(materias);

        return View(cadastrarVM);
    }

    [HttpPost("cadastrar")]
    public IActionResult Cadastrar(CadastrarQuestaoViewModel cadastrarVM)
    {
        List<Questao> questoes = repositorioQuestao.SelecionarRegistros();

        if (questoes.Any(q => q.Enunciado.Equals(cadastrarVM.Enunciado) && q.Materia.Id.Equals(cadastrarVM.MateriaId)))
            ModelState.AddModelError("ConflitoCadastro", "Já existe uma questão com este enunciado para a mesma matéria.");

        if (!ModelState.IsValid)
        {
            cadastrarVM.Materias = repositorioMateria.SelecionarRegistros()
                .Select(m => new SelectListItem { Text = m.Nome, Value = m.Id.ToString() })
                .ToList();

            return View(cadastrarVM);
        }

        Materia materiaSelecionada = repositorioMateria.SelecionarRegistroPorId(cadastrarVM.MateriaId)!;

        Questao novaQuestao = cadastrarVM.ParaEntidade(materiaSelecionada);

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioQuestao.CadastrarRegistro(novaQuestao);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerenciarAlternativas), new { id = novaQuestao.Id });
    }

    [HttpGet("editar/{id:guid}")]
    public IActionResult Editar(Guid id)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        List<Materia> materias = repositorioMateria.SelecionarRegistros();

        EditarQuestaoViewModel editarVM = new(
            questaoSelecionada,
            materias);

        return View(editarVM);
    }

    [HttpPost("editar/{id:guid}")]
    public IActionResult Editar(Guid id, EditarQuestaoViewModel editarVM)
    {
        List<Questao> questoes = repositorioQuestao.SelecionarRegistros();

        if (questoes.Any(q => q.Enunciado.Equals(editarVM.Enunciado) && q.Materia.Id.Equals(editarVM.MateriaId) && q.Id != id))
            ModelState.AddModelError("ConflitoEdicao", "Já existe uma questão com este enunciado para a mesma matéria.");

        if (!ModelState.IsValid)
        {
            editarVM.Materias = repositorioMateria.SelecionarRegistros()
                .Select(m => new SelectListItem { Text = m.Nome, Value = m.Id.ToString() })
                .ToList();

            return View(editarVM);
        }

        Materia materiaSelecionada = repositorioMateria.SelecionarRegistroPorId(editarVM.MateriaId)!;

        Questao questaoEditada = editarVM.ParaEntidade(materiaSelecionada);

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioQuestao.EditarRegistro(id, questaoEditada);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("excluir/{id:guid}")]
    public IActionResult Excluir(Guid id)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        ExcluirQuestaoViewModel excluirVM = new(
            id,
            questaoSelecionada.Enunciado);

        return View(excluirVM);
    }

    [HttpPost("excluir/{id:guid}")]
    public IActionResult ExcluirConfirmado(Guid id)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;
        List<Teste> testes = repositorioTeste.SelecionarRegistros();

        if (testes.Any(t => t.Questoes.Any(q => q.Id.Equals(id))))
        {
            ModelState.AddModelError("ConflitoExclusao", "Não é possível excluir a questão pois ela está vinculada a testes.");

            ExcluirQuestaoViewModel excluirVM = new()
            {
                Id = questaoSelecionada.Id,
                Enunciado = questaoSelecionada.Enunciado
            };

            return View("Excluir", excluirVM);
        }

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            repositorioQuestao.ExcluirRegistro(id);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }
        return RedirectToAction("Index");
    }

    [HttpGet, Route("/questoes/{id:guid}/detalhes")]
    public IActionResult Detalhes(Guid id)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        DetalhesQuestaoViewModel detalhesQuestaoVM = questaoSelecionada.ParaDetalhesVM();

        return View(detalhesQuestaoVM);
    }

    [HttpGet, Route("/questoes/{id:guid}/alternativas")]
    public IActionResult GerenciarAlternativas(Guid id)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        List<Alternativa> alternativas = questaoSelecionada.Alternativas.ToList();

        GerenciarAlternativasViewModel gerenciarAlternativasVM = new(
            questaoSelecionada,
            alternativas);

        if (alternativas.Count < 2 || alternativas.Count > 4)
            ModelState.AddModelError("ConflitoAlternativas", "A questão deve ter entre 2 e 4 alternativas.");

        if (alternativas.Count(a => a.EstaCorreta) != 1)
            ModelState.AddModelError("ConflitoAlternativas", "A questão deve ter exatamente uma alternativa correta.");

        if (!ModelState.IsValid)
            return View(gerenciarAlternativasVM);

        return View(gerenciarAlternativasVM);
    }

    [HttpPost, Route("/questoes/{id:guid}/adicionar-alternativa")]
    public IActionResult AdicionarAlternativa(Guid id, AdicionarAlternativaViewModel adicionarAlternativaVM)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        if (string.IsNullOrWhiteSpace(adicionarAlternativaVM.TextoAlternativa))
            ModelState.AddModelError("ConflitoAlternativas", "O texto da alternativa é obrigatório.");

        if (questaoSelecionada.Alternativas.Count >= 4)
            ModelState.AddModelError("ConflitoAlternativas", "A questão já possui o máximo de 4 alternativas.");

        if (questaoSelecionada.Alternativas.Any(a => a.Texto.Equals(adicionarAlternativaVM.TextoAlternativa)))
            ModelState.AddModelError("ConflitoAlternativas", "Essa alternativa já foi inserida.");

        if (!ModelState.IsValid)
        {
            GerenciarAlternativasViewModel gerenciarAlternativasVM = new(
                questaoSelecionada,
                questaoSelecionada.Alternativas.ToList());

            return View(nameof(GerenciarAlternativas), gerenciarAlternativasVM);
        }

        Alternativa novaAlternativa = new(
            adicionarAlternativaVM.TextoAlternativa,
            questaoSelecionada);

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            questaoSelecionada.AderirAlternativa(novaAlternativa);

            contexto.Alternativas.Add(novaAlternativa);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerenciarAlternativas), new { id });
    }

    [HttpPost, Route("/questoes/{id:guid}/remover-alternativa/{idAlternativa:guid}")]
    public IActionResult RemoverAlternativa(Guid id, Guid idAlternativa)
    {
        Questao questaoSelecionada = repositorioQuestao.SelecionarRegistroPorId(id)!;

        Alternativa alternativaEscolhida = repositorioQuestao.SelecionarAlternativa(questaoSelecionada, idAlternativa)!;

        IDbContextTransaction transacao = contexto.Database.BeginTransaction();

        try
        {
            questaoSelecionada.RemoverAlternativa(alternativaEscolhida);

            contexto.Alternativas.Remove(alternativaEscolhida);

            contexto.SaveChanges();

            transacao.Commit();
        }
        catch (Exception)
        {
            transacao.Rollback();

            throw;
        }

        return RedirectToAction(nameof(GerenciarAlternativas), new { id });
    }

    [HttpPost("/questoes/{id:guid}/marcar-alternativa-correta")]
    public IActionResult MarcarAlternativaCorreta(Guid id, Guid idAlternativaCorreta)
    {
        Questao questao = repositorioQuestao.SelecionarRegistroPorId(id)!;

        foreach (Alternativa a in questao.Alternativas)
            a.EstaCorreta = (a.Id.Equals(idAlternativaCorreta));

        contexto.SaveChanges();

        return RedirectToAction(nameof(GerenciarAlternativas), new { id });
    }

    [HttpPost, Route("/questoes/{id:guid}/finalizar")]
    public IActionResult FinalizarQuestao(Guid id)
    {
        Questao questao = repositorioQuestao.SelecionarRegistroPorId(id)!;

        if (questao.Alternativas.Count < 2 || questao.Alternativas.Count > 4)
            ModelState.AddModelError("ConflitoAlternativas", "A questão deve ter entre 2 e 4 alternativas.");

        if (questao.Alternativas.Count(a => a.EstaCorreta) != 1)
            ModelState.AddModelError("ConflitoAlternativas", "A questão deve ter exatamente uma alternativa correta.");

        if (!ModelState.IsValid)
        {
            GerenciarAlternativasViewModel gerenciarAlternativasVM = new(
                questao,
                questao.Alternativas.ToList());

            return View("GerenciarAlternativas", gerenciarAlternativasVM);
        }

        questao.Finalizado = true;

        contexto.SaveChanges();

        return RedirectToAction(nameof(Index));
    }
}
